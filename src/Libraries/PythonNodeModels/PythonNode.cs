using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Autodesk.DesignScript.Runtime;
using Dynamo.Configuration;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.ZeroTouch;
using Dynamo.Models;
using Dynamo.PythonServices;
using Dynamo.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ProtoCore.AST.AssociativeAST;

namespace PythonNodeModels
{
    /// <summary>
    /// Event arguments used to send the original and migrated code to the ScriptEditor
    /// </summary>
    internal class PythonCodeMigrationEventArgs : EventArgs
    {
        public string OldCode { get; private set; }
        public string NewCode { get; private set; }
        public PythonCodeMigrationEventArgs(string oldCode, string newCode)
        {
            OldCode = oldCode;
            NewCode = newCode;
        }
    }

    public abstract class PythonNodeBase : NodeModel
    {
        private string engine = string.Empty;

        // Set the default EngineName value to IronPython2 so that older graphs can show the migration warnings.
        [DefaultValue("IronPython2")]
        [JsonProperty("Engine", DefaultValueHandling = DefaultValueHandling.Populate)]
        /// <summary>
        /// Return the user selected python engine enum.
        /// </summary>
        public string EngineName
        {
            get
            {
                // This is a first-time case for newly created nodes only
                if (string.IsNullOrEmpty(engine))
                {
                    SetEngineByDefault();
                }
                return engine;
            }
            set
            {
                if (engine != value)
                {
                    engine = value;
                    RaisePropertyChanged(nameof(EngineName));
                }   
            }
        }

        /// <summary>
        /// The method returns the assembly name from which the node originated.
        /// </summary>
        /// <returns>Assembly Name</returns>
        internal override AssemblyName GetNameOfAssemblyReferencedByNode()
        {
            var pyEng = PythonEngineManager.Instance.AvailableEngines.Where(x => x.Name.Equals(this.EngineName)).FirstOrDefault();
            if (pyEng != null)
            {
                NameOfAssemblyReferencedByNode = pyEng.GetType().Assembly.GetName();
            }
            return NameOfAssemblyReferencedByNode;
        }

        /// <summary>
        /// Set the engine to be used by default for this node, based on user and system settings.
        /// </summary>
        private void SetEngineByDefault()
        {
            var version = PreferenceSettings.GetDefaultPythonEngine();
            var systemDefault = DynamoModel.DefaultPythonEngine;
            if (!string.IsNullOrEmpty(version))
            {
                engine = version;
            }
            else if (!string.IsNullOrEmpty(systemDefault))
            {
                engine = systemDefault;
            }
            else
            {
                // Use PythonNet3 as default
                engine = PythonEngineManager.PythonNet3EngineName;
            }
        }

        protected PythonNodeBase()
        {
            InPorts.Add(new PortModel(PortType.Input, this, new PortData("a", "a")));
            OutPorts.Add(new PortModel(PortType.Output, this, new PortData("result", Properties.Resources.PythonNodePortDataOutputToolTip)));
            ArgumentLacing = LacingStrategy.Disabled;
        }

        /// <summary>
        /// Private constructor used for serialization.
        /// </summary>
        /// <param name="inPorts">A collection of <see cref="PortModel"/> objects.</param>
        /// <param name="outPorts">A collection of <see cref="PortModel"/> objects.</param>
        protected PythonNodeBase(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts) : base(inPorts, outPorts)
        {
            ArgumentLacing = LacingStrategy.Disabled;
        }

        protected List<AssociativeNode> CreateOutputAST(
            AssociativeNode codeInputNode, List<AssociativeNode> inputAstNodes,
            List<Tuple<string, AssociativeNode>> additionalBindings)
        {
            var names =
                additionalBindings.Select(
                    x => AstFactory.BuildStringNode(x.Item1) as AssociativeNode).ToList();
            names.Add(AstFactory.BuildStringNode("IN"));

            var vals = additionalBindings.Select(x => x.Item2).ToList();
            vals.Add(AstFactory.BuildExprList(inputAstNodes));

            var functionCallIdentifier = AstFactory.BuildIdentifier(GUID + "_packed");
            var functionCall = AstFactory.BuildFunctionCall(
                    "PythonEvaluator",
                    "Evaluate",
                    new List<AssociativeNode>
                    {
                        AstFactory.BuildStringNode(GUID.ToString()),
                        AstFactory.BuildStringNode(EngineName),
                        codeInputNode,
                        AstFactory.BuildExprList(names),
                        AstFactory.BuildExprList(vals)
                    });
            var astnode = AstFactory.BuildAssignment(
                functionCallIdentifier, functionCall
                );
            if (OutPorts.Count == 1) {
                return new List<AssociativeNode>{
                    AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(0), astnode)
                };
            }
            else
            {
                var result = new List<AssociativeNode>();
                for (int i = 0; i < OutPorts.Count; i++)
                {
                    var extractorCall = AstFactory.BuildFunctionCall(
                            "PythonEvaluator",
                            "GetTupleField",
                            new List<AssociativeNode>
                            {
                        functionCall,
                        AstFactory.BuildIntNode(i+1L),
                    });
                    result.Add(AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(i), extractorCall));
                }
                return result;
            }
        }

        internal event EventHandler MigrationAssistantRequested;
        internal void RequestCodeMigration(EventArgs e)
        {
            MigrationAssistantRequested?.Invoke(this, e);
        }

        protected override bool UpdateValueCore(UpdateValueParams updateValueParams)
        {
            string name = updateValueParams.PropertyName;
            string value = updateValueParams.PropertyValue;

            if (name == nameof(EngineName))
            {
                EngineName = value;
                return true;

            }
            return base.UpdateValueCore(updateValueParams);
        }

    }

    [NodeName("C# Script")]
    [NodeCategory(BuiltinNodeCategories.CORE_SCRIPTING)]
    [NodeDescription("PythonScriptDescription", typeof(Properties.Resources))]
    [NodeSearchTags("PythonSearchTags", typeof(Properties.Resources))]
    [OutPortTypes("var[]..[]")]
    [SupressImportIntoVM]
    [IsDesignScriptCompatible]
    public sealed class PythonNode : PythonNodeBase
    {
        private bool showAutoUpgradedBar;
        /// <summary>
        /// This property is true if the node was auto-upgraded from CPython3 to PythonNet3.
        /// </summary>
        [JsonIgnore]
        public bool ShowAutoUpgradedBar
        {
            get => showAutoUpgradedBar;
            internal set
            {
                if (showAutoUpgradedBar != value)
                {
                    showAutoUpgradedBar = value;
                    RaisePropertyChanged(nameof(ShowAutoUpgradedBar));
                }
            }
        }

        /// <summary>
        /// The NodeType property provides a name which maps to the 
        /// server type for the node. This property should only be
        /// used for serialization. 
        /// </summary>
        public override string NodeType
        {
            get
            {
                return "PythonScriptNode";
            }
        }

        /// <summary>
        /// The default Python code template. Code comments are saved in *.resx for localisation.
        /// </summary>
        private string defaultPythonTemplateCode
        {
            get
            {
                return
@"static object Main(double a) {
    return a*a;
}";
            }
        }

        /// <summary>
        /// Private constructor used for serialization.
        /// </summary>
        /// <param name="inPorts">A collection of <see cref="PortModel"/> objects.</param>
        /// <param name="outPorts">A collection of <see cref="PortModel"/> objects.</param>
        [JsonConstructor]
        private PythonNode(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts) : base(inPorts, outPorts) { }

        public PythonNode()
        {
            var pythonTemplatePath = PreferenceSettings.GetPythonTemplateFilePath();
            if (!String.IsNullOrEmpty(pythonTemplatePath) && File.Exists(pythonTemplatePath))
                script = File.ReadAllText(pythonTemplatePath);
            else
                script = defaultPythonTemplateCode;
        }

        private string script;

        [JsonProperty("Code")]
        public string Script
        {
            get { return script; }
            set
            {
                if (script != value)
                {
                    script = value;
                    RaisePropertyChanged("Script");
                }
            }
        }

        public override IEnumerable<AssociativeNode> BuildOutputAst(
            List<AssociativeNode> inputAstNodes)
        {
            return CreateOutputAST(
                    AstFactory.BuildStringNode(script),
                    inputAstNodes,
                    new List<Tuple<string, AssociativeNode>>()
                    {
                        Tuple.Create<string, AssociativeNode>(nameof(Name), AstFactory.BuildStringNode(Name))
                    });
        }

        protected override bool UpdateValueCore(UpdateValueParams updateValueParams)
        {
            string name = updateValueParams.PropertyName;
            string value = updateValueParams.PropertyValue;

            if (name == "ScriptContent")
            {
                script = value;
                return true;
            }

            return base.UpdateValueCore(updateValueParams);
        }

        [Obsolete("This method is part of the temporary IronPython to CPython3 migration feature and will be removed in future versions of Dynamo.")]
        /// <summary>
        /// Updates the Script property of the node and raise the migration event notifications.
        /// NOTE: This is a temporary method used during the Python 2 to Python 3 transistion period,
        /// it will be removed when the transistion period is over.
        /// </summary>
        /// <param name="newCode">The new migrated code</param>
        internal void MigrateCode(string newCode)
        {        
            var e = new PythonCodeMigrationEventArgs(Script, newCode); 
            Script = newCode;
            OnCodeMigrated(e);
        }

        /// <summary>
        //  Boolean to check if script content is saved or not.
        /// </summary>
        internal bool ScriptContentSaved = true;

        // Event triggered when this node is edited.
        internal event Action<string> EditNode;

        // Event triggered when the script editor is not saved and shows a warning when closed.
        internal event Action UserScriptWarned;

        /// <summary>
        /// This is called to edit the python node script.
        /// </summary>
        public void OnNodeEdited(string content)
        {
            EditNode?.Invoke(content);
        }

        /// <summary>
        /// This is called to show a warning that the script editor is not saved yet.
        /// </summary>
        public void OnWarnUserScript()
        {
            UserScriptWarned?.Invoke();
        }

        internal List<Delegate> GetInvocationListForEditAction()
        {
            var delegates = new List<Delegate>();
            if (EditNode != null)
            {
                delegates = EditNode.GetInvocationList().ToList();
            }

            return delegates;
        }

        /// <summary>
        /// Fires when the Script content is migrated to Python 3.
        /// NOTE: This is a temporary event used during the Python 2 to Python 3 transistion period,
        /// it will be removed when the transistion period is over.
        /// </summary>
        internal event EventHandler<PythonCodeMigrationEventArgs> CodeMigrated;
        private void OnCodeMigrated(PythonCodeMigrationEventArgs e)
        {
            CodeMigrated?.Invoke(this, e);
        }

        #region SerializeCore/DeserializeCore

        [Obsolete]
        protected override void SerializeCore(XmlElement element, SaveContext context)
        {
            base.SerializeCore(element, context);

            XmlElement script = element.OwnerDocument.CreateElement("Script");
            script.InnerText = this.script;
            element.AppendChild(script);
            XmlElement engine = element.OwnerDocument.CreateElement(nameof(EngineName));
            engine.InnerText = EngineName;
            element.AppendChild(engine);

        }

        [Obsolete]
        protected override void DeserializeCore(XmlElement nodeElement, SaveContext context)
        {
            base.DeserializeCore(nodeElement, context);

            var scriptNode =
                nodeElement.ChildNodes.Cast<XmlNode>().FirstOrDefault(x => x.Name == "Script");

            if (scriptNode != null)
            {
                script = scriptNode.InnerText;
            }
            var engineNode =
              nodeElement.ChildNodes.Cast<XmlNode>().FirstOrDefault(x => x.Name == nameof(EngineName));

            if (engineNode != null)
            {
                string oldEngine = EngineName;
                EngineName = engineNode.InnerText;
                if (context == SaveContext.Undo && oldEngine != this.EngineName)
                {
                    // For Python nodes, changing the Engine property does not set the node Modified flag.
                    // This is done externally in all event handlers, so for Undo we do it here.
                    OnNodeModified();
                }
            }
        }

        private record MainSignature(List<string> Inputs, List<string> Outputs);

        private static MainSignature FindMainSignature(SyntaxNode root)
        {
            var main = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m =>
                    m.Identifier.Text == "Main" &&
                    m.Modifiers.Any(SyntaxKind.StaticKeyword)).FirstOrDefault();
            if (main == null) return null;

            var inputs = new List<string>();
            var outputs = new List<string>();

            // --- INPUT PARAMETERS ---
            foreach (var p in main.ParameterList.Parameters)
                inputs.Add(p.Identifier.Text);

            // --- OUTPUT TUPLE ---
            if (main.ReturnType is TupleTypeSyntax tupleType)
            {
                foreach (var element in tupleType.Elements)
                {
                    var name = element.Identifier.Text;
                    if (string.IsNullOrWhiteSpace(name))
                        name = "__unnamed_tuple_element__";

                    outputs.Add(name);
                }
            }
            else
            {
                // Not a tuple → provide a default output name
                outputs.Add("result");
            }

            return new MainSignature(inputs, outputs);
        }
        private static MainSignature FindMainSignature(string code)
        {
            try
            {
                // try parsing as a script (.csx)
                var scriptTree = CSharpSyntaxTree.ParseText(
                    code,
                    new CSharpParseOptions(kind: SourceCodeKind.Script)
                );
                var scriptRoot = scriptTree.GetRoot();

                var signature = FindMainSignature(scriptRoot);
                if(signature?.Inputs.Any(string.IsNullOrEmpty) == true || signature?.Outputs.Any(string.IsNullOrEmpty) == true)
                {
                    return null;
                }
                return signature;
            }
            catch(Exception ex)
            {
                return null;
            }
        }
        MainSignature signature;

        void Synchronize(ObservableCollection<PortModel> ports, List<string> targetList, PortType portType)
        {

            bool update = false;
            if (ports.Count == targetList.Count)
            {
                for (int i = 0; i < ports.Count; ++i)
                {
                    if (ports[i].Name != targetList[i])
                    {
                        update = true;
                    }
                }
            }
            else
            {
                update = true;
            }
            if (update)
            {
                while (ports.Count > 0)
                {
                    var port = ports[ports.Count - 1];
                    port.DestroyConnectors();
                    ports.Remove(port);
                }
                while (ports.Count < targetList.Count)
                {
                    var idx = ports.Count;
                    ports.Add(new PortModel(portType, this, new PortData(targetList[idx], targetList[idx])));
                }
            }
        }

        internal bool OnParametersModified(string script)
        {
            signature = FindMainSignature(script);
            if(signature == null)
            {
                return false;
            }
            Synchronize(InPorts, signature.Inputs, PortType.Input);
            Synchronize(OutPorts, signature.Outputs, PortType.Output);
            ClearErrorsAndWarnings();
            return true;
        }

        #endregion
    }

    [NodeName("Python Script From String")]
    [NodeCategory(BuiltinNodeCategories.CORE_SCRIPTING)]
    [NodeDescription("PythonScriptFromStringDescription", typeof(Properties.Resources))]
    [NodeSearchTags("PythonSearchTags", typeof(Properties.Resources))]
    [OutPortTypes("var[]..[]")]
    [SupressImportIntoVM]
    [IsDesignScriptCompatible]
    public sealed class PythonStringNode : PythonNodeBase
    {
        /// <summary>
        /// Private constructor used for serialization.
        /// </summary>
        /// <param name="inPorts">A collection of <see cref="PortModel"/> objects.</param>
        /// <param name="outPorts">A collection of <see cref="PortModel"/> objects.</param>
        [JsonConstructor]
        private PythonStringNode(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts) : base(inPorts, outPorts) { }

        public PythonStringNode()
        {
            InPorts.Add(new PortModel(PortType.Input, this, new PortData("script", Properties.Resources.PythonStringPortDataScriptToolTip)));
            RegisterAllPorts();
        }

        public override IEnumerable<AssociativeNode> BuildOutputAst(
            List<AssociativeNode> inputAstNodes)
        {
            return CreateOutputAST(
                    inputAstNodes[0],
                    inputAstNodes.Skip(1).ToList(),
                    new List<Tuple<string, AssociativeNode>>()
                    {
                        Tuple.Create<string, AssociativeNode>(nameof(Name), AstFactory.BuildStringNode(Name))
                    });
        }
    }
}

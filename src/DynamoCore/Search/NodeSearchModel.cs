using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Dynamo.Configuration;
using Dynamo.Graph.Nodes;
using Dynamo.Logging;
using Dynamo.Search.SearchElements;
using Dynamo.Utilities;
using DynamoUtilities;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;

namespace Dynamo.Search
{
    /// <summary>
    ///     Searchable library of NodeSearchElements that can produce NodeModels.
    /// </summary>
    public class NodeSearchModel : SearchLibrary<NodeSearchElement, NodeModel>
    {
        /// <summary>
        ///     Construct a NodeSearchModel object
        /// </summary>
        /// <param name="logger"> (Optional) A logger to pass through to SearchLibrary for logging search data</param>
        internal NodeSearchModel(ILogger logger = null) : base(logger)
        {
        }

        internal override void Add(NodeSearchElement entry)
        {
            SearchElementGroup group = SearchElementGroup.None;

            entry.FullCategoryName = ProcessNodeCategory(entry.FullCategoryName, ref group);
            entry.Group = group;

            base.Add(entry);
        }

        /// <summary>
        ///     Dumps the contents of search into an Xml file.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="dynamoPath"></param>
        internal void DumpLibraryToXml(string fileName, string dynamoPath)
        {
            if (string.IsNullOrEmpty(fileName))
                return;

            var document = ComposeXmlForLibrary(dynamoPath);
            document.Save(fileName);
        }

        /// <summary>
        ///     Serializes the contents of search into Xml.
        /// </summary>
        /// <returns></returns>
        internal XmlDocument ComposeXmlForLibrary(string dynamoPath)
        {
            var document = XmlHelper.CreateDocument("LibraryTree");

            var root = SearchCategoryUtil.CategorizeSearchEntries(
                Entries,
                entry => entry.Categories);

            foreach (var category in root.SubCategories)
                AddCategoryToXml(document.DocumentElement, category, dynamoPath);

            foreach (var entry in root.Entries)
                AddEntryToXml(document.DocumentElement, entry, dynamoPath);

            return document;
        }

        private static void AddEntryToXml(XmlNode parent, NodeSearchElement entry, string dynamoPath)
        {
            var element = XmlHelper.AddNode(parent, entry.GetType().ToString());
            XmlHelper.AddNode(element, "FullCategoryName", entry.FullCategoryName);
            XmlHelper.AddNode(element, "Name", entry.Name);
            XmlHelper.AddNode(element, "Group", entry.Group.ToString());
            XmlHelper.AddNode(element, "Description", entry.Description);

            // Add search tags, joined by ",".
            // E.g. <SearchTags>bounding,bound,bymaxmin,max,min,bypoints</SearchTags>
            XmlHelper.AddNode(element, "SearchTags",
                String.Join(",", entry.SearchKeywords.Where(word => !String.IsNullOrWhiteSpace(word))));

            var dynamoNode = entry.CreateNode();

            // If entry has input parameters.
            if (dynamoNode.InPorts.Count != 0)
            {
                var inputNode = XmlHelper.AddNode(element, "Inputs");
                for (int i = 0; i < dynamoNode.InPorts.Count; i++)
                {
                    var parameterNode = XmlHelper.AddNode(inputNode, "InputParameter");

                    XmlHelper.AddAttribute(parameterNode, "Name", dynamoNode.InPorts[i].Name);

                    // Case for UI nodes as ColorRange, List.Create etc.
                    // UI nodes  do not have incoming ports in NodeSearchElement, but do have incoming ports in NodeModel.
                    // UI node initializes its incoming ports, when its constructor is called.
                    // So, here we check if NodeModel has input ports and NodeSearchElement also has the same input ports.
                    // UI node node will have several incoming ports on NodeModel,
                    // but 0 incoming ports on NodeSearchElement 
                    // (when there is no incoming port, it returns none 1st port by default).
                    if (dynamoNode.InPorts.Count == entry.InputParameters.Count()
                        && entry.InputParameters.First().Item2 != Properties.Resources.NoneString)
                    {
                        XmlHelper.AddAttribute(parameterNode, "Type", entry.InputParameters.ElementAt(i).Item2);
                    }

                    // Add default value, if it's not null.
                    if (dynamoNode.InPorts[i].DefaultValue != null)
                    {
                        XmlHelper.AddAttribute(parameterNode, "DefaultValue",
                            dynamoNode.InPorts[i].DefaultValue.ToString());
                    }
                    XmlHelper.AddAttribute(parameterNode, "Tooltip", dynamoNode.InPorts[i].ToolTip);
                }
            }

            // If entry has output parameters.
            if (dynamoNode.OutPorts.Count != 0)
            {
                var outputNode = XmlHelper.AddNode(element, "Outputs");
                for (int i = 0; i < dynamoNode.OutPorts.Count; i++)
                {
                    var parameterNode = XmlHelper.AddNode(outputNode, "OutputParameter");
                    XmlHelper.AddAttribute(parameterNode, "Name", dynamoNode.OutPorts[i].Name);

                    // Case for UI nodes as ColorRange.
                    if (dynamoNode.OutPorts.Count == entry.OutputParameters.Count()
                        && entry.OutputParameters.First() != Properties.Resources.NoneString)
                    {
                        XmlHelper.AddAttribute(parameterNode, "Type", entry.OutputParameters.ElementAt(i));
                    }

                    XmlHelper.AddAttribute(parameterNode, "Tooltip", dynamoNode.OutPorts[i].ToolTip);
                }
            }

            var assemblyName = Path.GetFileNameWithoutExtension(entry.Assembly);
            const string resourcesPath = @"src\Resources\";

            // Get icon paths.
            string pathToSmallIcon = Path.Combine(
                resourcesPath,
                assemblyName,
                "SmallIcons", entry.IconName + ".Small.png");

            string pathToLargeIcon = Path.Combine(
               resourcesPath,
               assemblyName,
               "LargeIcons", entry.IconName + ".Large.png");

            if (!File.Exists(Path.Combine(dynamoPath, @"..\..\..\", pathToSmallIcon)))
            {
                // Try DynamoCore path.
                pathToSmallIcon = Path.Combine(
                   resourcesPath,
                    "DynamoCore",
                    "SmallIcons", entry.IconName + ".Small.png");
            }

            if (!File.Exists(Path.Combine(dynamoPath, @"..\..\..\", pathToLargeIcon)))
            {
                // Try DynamoCore path.
                pathToLargeIcon = Path.Combine(
                    resourcesPath,
                    "DynamoCore",
                    "LargeIcons", entry.IconName + ".Large.png");
            }

            // Dump icons.
            XmlHelper.AddNode(element, "SmallIcon", File.Exists(Path.Combine(dynamoPath, @"..\..\..\", pathToSmallIcon)) ? pathToSmallIcon : "Not found");
            XmlHelper.AddNode(element, "LargeIcon", File.Exists(Path.Combine(dynamoPath, @"..\..\..\", pathToLargeIcon)) ? pathToLargeIcon : "Not found");
        }

        private static void AddCategoryToXml(
            XmlNode parent, ISearchCategory<NodeSearchElement> category, string dynamoPath)
        {
            var element = XmlHelper.AddNode(parent, "Category");
            XmlHelper.AddAttribute(element, "Name", category.Name);

            foreach (var subCategory in category.SubCategories)
                AddCategoryToXml(element, subCategory, dynamoPath);

            foreach (var entry in category.Entries)
                AddEntryToXml(element, entry, dynamoPath);
        }

        /// <summary>
        /// Call this method to assign a default grouping information if a given category 
        /// does not have any. A node category's group can either be "Create", "Query" or
        /// "Actions". If none of the group names above is assigned to the category, it 
        /// will be assigned a default one that is "Actions".
        /// 
        /// For examples:
        /// 
        ///     "Core.Evaluate" will be renamed as "Core.Evaluate.Actions"
        ///     "Core.List.Create" will remain as "Core.List.Create"
        /// 
        /// </summary>
        internal string ProcessNodeCategory(string category, ref SearchElementGroup group)
        {
            if (string.IsNullOrEmpty(category))
                return category;

            int index = category.LastIndexOf(Configurations.CategoryDelimiterString);

            // If "index" is "-1", then the whole "category" will be used as-is.            
            switch (category.Substring(index + 1))
            {
                case Configurations.CategoryGroupAction:
                    group = SearchElementGroup.Action;
                    break;
                case Configurations.CategoryGroupCreate:
                    group = SearchElementGroup.Create;
                    break;
                case Configurations.CategoryGroupQuery:
                    group = SearchElementGroup.Query;
                    break;
                default:
                    group = SearchElementGroup.Action;
                    return category;
            }

            return category.Substring(0, index);
        }

        /// <summary>
        /// Search for nodes by using a search key.
        /// </summary>
        /// <param name="search"></param>
        /// <param name="luceneSearchUtility"></param>
        /// <param name="ctk">Cancellation token to short circuit the search.</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        internal IEnumerable<NodeSearchElement> Search(string search, LuceneSearchUtility luceneSearchUtility, CancellationToken ctk = default)
        {
            ctk.ThrowIfCancellationRequested();

            if (luceneSearchUtility != null)
            {
                luceneSearchUtility.dirReader = luceneSearchUtility.writer != null ? luceneSearchUtility.writer.GetReader(applyAllDeletes: true) : DirectoryReader.Open(luceneSearchUtility.indexDir);
                luceneSearchUtility.Searcher = new(luceneSearchUtility.dirReader);

                string searchTerm = search.Trim();
                var candidates = new List<NodeSearchElement>();

                var parser = new MultiFieldQueryParser(LuceneConfig.LuceneNetVersion, LuceneConfig.NodeIndexFields, luceneSearchUtility.Analyzer)
                {
                    AllowLeadingWildcard = true,
                    DefaultOperator = LuceneConfig.DefaultOperator,
                    FuzzyMinSim = LuceneConfig.MinimumSimilarity
                };

                Query query = parser.Parse(luceneSearchUtility.CreateSearchQuery(LuceneConfig.NodeIndexFields, searchTerm, false, ctk));
                TopDocs topDocs = luceneSearchUtility.Searcher.Search(query, n: LuceneConfig.DefaultResultsCount);

                for (int i = 0; i < topDocs.ScoreDocs.Length; i++)
                {
                    ctk.ThrowIfCancellationRequested();

                    // read back a Lucene doc from results
                    Document resultDoc = luceneSearchUtility.Searcher.Doc(topDocs.ScoreDocs[i].Doc);

                    string name = resultDoc.Get(nameof(LuceneConfig.NodeFieldsEnum.Name));
                    string docName = resultDoc.Get(nameof(LuceneConfig.NodeFieldsEnum.DocName));
                    string cat = resultDoc.Get(nameof(LuceneConfig.NodeFieldsEnum.FullCategoryName));
                    string parameters = resultDoc.Get(nameof(LuceneConfig.NodeFieldsEnum.Parameters));

                    if (!string.IsNullOrEmpty(docName))
                    {
                        //code for setting up documentation info
                    }
                    else
                    {
                        var foundNode = FindModelForNodeNameAndCategory(name, cat, parameters, ctk);
                        if (foundNode != null)
                        {
                            candidates.Add(foundNode);
                        }
                    }
                }
                return candidates;
            }
            return null;
        }

        /// <summary>
        /// Finds the node model that corresponds to the input nodeName, nodeCategory and parameters.
        /// </summary>
        /// <param name="nodeName"></param>
        /// <param name="nodeCategory"></param>
        /// <param name="parameters"></param>
        /// <param name="ctk">Cancellation token to short circuit the operation.</param>
        /// <returns></returns>
        internal NodeSearchElement FindModelForNodeNameAndCategory(string nodeName, string nodeCategory, string parameters, CancellationToken ctk = default)
        {
            ctk.ThrowIfCancellationRequested();

            var result = Entries.Where(e => {
                if (e.Name.Replace(" ", string.Empty).Equals(nodeName) && e.FullCategoryName.Equals(nodeCategory))
                {
                    //When the node info was indexed if Parameters was null we added an empty space (null cannot be indexed)
                    //Then in this case when searching if e.Parameters is null we need to check against empty space
                    if (e.Parameters == null)
                        return string.IsNullOrEmpty(parameters);
                    //Parameters contain a value so we need to compare against the value indexed
                    else
                        return e.Parameters.Equals(parameters);
                }
                return false;
            });

            if (!result.Any())
            {
                return null;
            }

            return result.ElementAt(0);
        }
    }
}

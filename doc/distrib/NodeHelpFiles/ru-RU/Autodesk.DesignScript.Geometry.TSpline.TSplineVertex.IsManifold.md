## In-Depth
В приведенном ниже примере неоднородная поверхность создается путем объединения двух поверхностей с общим внутренним ребром. Итоговая поверхность не имеет ни четкой передней, ни четкой задней части. Неоднородная поверхность отображается только в режиме рамки до тех пор, пока не будет восстановлена. Узел `TSplineTopology.DecomposedVertices` используется для запроса всех вершин поверхности, а узел `TSplineVertex.IsManifold` выделяет вершины, которые можно считать однородными. Неоднородные вершины извлекаются, а их положение визуализируется с помощью узлов `TSplineVertex.UVNFrame` и `TSplineUVNFrame.Position`.


## Файл примера

![Example](./Autodesk.DesignScript.Geometry.TSpline.TSplineVertex.IsManifold_img.jpg)

## 상세
'Mesh.ByPointsIndices'는 메쉬 삼각형의 `정점`을 나타내는 `점` 리스트와 메쉬가 함께 스티치되는 방식을 나타내는 `색인` 리스트를 사용하여 새 메쉬를 작성합니다. `점` 입력은 메쉬를 구성하는 고유한 정점의 단순 리스트여야 합니다. `색인` 입력은 정수로 이루어진 단순 리스트여야 합니다. 3개의 정수 집합은 각각 메쉬에서 삼각형을 지정합니다. 정수는 정점 리스트에서 정점의 색인을 지정합니다. 색인은 0부터 시작해야 하며 정점 리스트의 첫 번째 점은 색인이 0이어야 합니다.

아래 예제에서는 `Mesh.ByPointsIndices` 노드를 사용하여 메쉬를 작성합니다. 이때 9개의 `점` 리스트와 36개의 `색인` 리스트를 사용하며, 이 색인 리스트는 메쉬를 구성하는 12개의 삼각형 각각에 대한 정점 조합을 지정합니다.

## 예제 파일

![Example](./Autodesk.DesignScript.Geometry.Mesh.ByPointsIndices_img.png)

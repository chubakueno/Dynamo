## Подробности
`List.GroupByFunction` возвращает новый список, сгруппированный по функции.

Для входного параметра `groupFunction` требуется узел в состоянии функции (т. е. возвращается функция). Это означает, что по крайней мере один из входных параметров узла не подключен. Затем Dynamo запускает функцию узла для каждого элемента во входном списке `List.GroupByFunction`, чтобы использовать выходные данные как механизм группирования.

В приведенном ниже примере два различных списка сгруппированы с помощью функции `List.GetItemAtIndex`. Эта функция создает группы (новый список) из каждого индекса верхнего уровня.
___
## Файл примера

![List.GroupByFunction](./List.GroupByFunction_img.jpg)

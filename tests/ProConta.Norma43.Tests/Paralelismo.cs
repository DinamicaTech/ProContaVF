// Las pruebas contra Access se ejecutan de una en una: una sola copia de ACE por proceso (con varias clases a la vez el proceso de pruebas se caía).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

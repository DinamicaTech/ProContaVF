// Las pruebas contra Access se ejecutan de una en una: una sola copia de ACE por proceso (varias en paralelo rompen el controlador).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

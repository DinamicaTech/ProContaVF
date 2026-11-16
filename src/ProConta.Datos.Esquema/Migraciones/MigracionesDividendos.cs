namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por contabilidad/procesos/aplicacion-resultado (socios y reparto de dividendos).</summary>
public static class MigracionesDividendos
{
    /// <summary>
    /// Socios y reparto de dividendos (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): el rol Socio de los terceros con su %
    /// de participación y sus subcuentas 526 (dividendo a pagar) y 4751 (retención) por sociedad (terceros/roles), cada reparto con su detalle
    /// por socio y, en SociedadesEjercicios, el % de retención de dividendos y la cuenta de reservas de la que salen (vacíos: 19 % y 1130000).
    /// Solo estructura, sin datos.
    /// </summary>
    public static Migracion Dividendos { get; } = new(
        "20261116_1800_DIVIDENDOS", 261116018,
        "Socios por sociedad, repartos de dividendos y retención y reservas de dividendos del ejercicio.",
        "contabilidad/procesos/aplicacion-resultado",
        new CrearTabla("Socios",
            "CREATE TABLE Socios (IdSocio COUNTER CONSTRAINT PK_Socios PRIMARY KEY, " +
            "IdTercero LONG NOT NULL CONSTRAINT FK_Socios_Tercero REFERENCES Terceros (IdTercero), " +
            "Activo YESNO NOT NULL, FechaAlta DATETIME NOT NULL, Notas LONGTEXT)"),
        new CrearIndice("Socios", "UX_Socios_Tercero", "CREATE UNIQUE INDEX UX_Socios_Tercero ON Socios (IdTercero)"),
        new CrearTabla("SociosSociedad",
            "CREATE TABLE SociosSociedad (IdSocioSociedad COUNTER CONSTRAINT PK_SociosSociedad PRIMARY KEY, " +
            "IdSocio LONG NOT NULL CONSTRAINT FK_SociosSociedad_Socio REFERENCES Socios (IdSocio), " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_SociosSociedad_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Participacion DECIMAL(9,4), " +
            "IdIdentidadDividendos LONG CONSTRAINT FK_SociosSociedad_Dividendos REFERENCES IdentidadesContables (IdIdentidadContable), " +
            "IdIdentidadRetencion LONG CONSTRAINT FK_SociosSociedad_Retencion REFERENCES IdentidadesContables (IdIdentidadContable), " +
            "Activo YESNO NOT NULL)"),
        new CrearIndice("SociosSociedad", "UX_SociosSociedad_SocioSociedad",
            "CREATE UNIQUE INDEX UX_SociosSociedad_SocioSociedad ON SociosSociedad (IdSocio, IdSociedad)"),
        new CrearTabla("RepartosDividendos",
            "CREATE TABLE RepartosDividendos (IdRepartoDividendos COUNTER CONSTRAINT PK_RepartosDividendos PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_RepartosDividendos_Ejercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "Fecha DATETIME NOT NULL, PorcentajeRetencion DECIMAL(9,4) NOT NULL, CuentaReservas TEXT(10) NOT NULL, " +
            "Neto CURRENCY NOT NULL, Bruto CURRENCY NOT NULL, Retencion CURRENCY NOT NULL, Motivo TEXT(255), IdAsiento LONG, " +
            "Operador TEXT(100), FechaHoraConfirmacion DATETIME NOT NULL)"),
        new CrearIndice("RepartosDividendos", "IX_RepartosDividendos_Ejercicio",
            "CREATE INDEX IX_RepartosDividendos_Ejercicio ON RepartosDividendos (IdSociedadEjercicio, Fecha)"),
        new CrearTabla("RepartosDividendosSocios",
            "CREATE TABLE RepartosDividendosSocios (IdRepartoDividendosSocio COUNTER CONSTRAINT PK_RepartosDividendosSocios PRIMARY KEY, " +
            "IdRepartoDividendos LONG NOT NULL CONSTRAINT FK_RepartosDividendosSocios_Reparto REFERENCES RepartosDividendos (IdRepartoDividendos), " +
            "IdSocio LONG NOT NULL CONSTRAINT FK_RepartosDividendosSocios_Socio REFERENCES Socios (IdSocio), " +
            "Participacion DECIMAL(9,4), Neto CURRENCY NOT NULL, Bruto CURRENCY NOT NULL, Retencion CURRENCY NOT NULL, " +
            "CuentaDividendos TEXT(10) NOT NULL, CuentaRetencion TEXT(10) NOT NULL)"),
        new CrearIndice("RepartosDividendosSocios", "UX_RepartosDividendosSocios_RepartoSocio",
            "CREATE UNIQUE INDEX UX_RepartosDividendosSocios_RepartoSocio ON RepartosDividendosSocios (IdRepartoDividendos, IdSocio)"),
        new AnadirColumna("SociedadesEjercicios", "PorcentajeRetencionDividendos", "DECIMAL(9,4)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaReservasDividendos", "TEXT(10)"))
    {
        Descripciones =
        [
            new("Socios", null, "Rol Socio de un tercero (terceros/roles): la persona que participa en el capital de una sociedad y cobra dividendos."),
            new("Socios", "IdSocio", "Identificador del rol Socio."),
            new("Socios", "IdTercero", "Tercero que tiene el rol (uno como mucho por tercero)."),
            new("Socios", "Activo", "Si el rol está activo en alguna sociedad; al quitarlo la fila se conserva."),
            new("Socios", "FechaAlta", "Día en que se dio el rol."),
            new("Socios", "Notas", "Notas del rol."),
            new("SociosSociedad", null, "Datos del socio en cada sociedad: su % de participación vigente y sus subcuentas 526 y 4751 (terceros/roles). Los cambios del % quedan en el historial de la ficha."),
            new("SociosSociedad", "IdSocioSociedad", "Identificador de la fila."),
            new("SociosSociedad", "IdSocio", "Socio."),
            new("SociosSociedad", "IdSociedad", "Sociedad de la que es socio."),
            new("SociosSociedad", "Participacion", "Participación vigente en el capital, en porcentaje (50 = 50 %). Reparte los dividendos y los pagos entre socios."),
            new("SociosSociedad", "IdIdentidadDividendos", "Subcuenta 526 (dividendo activo a pagar) del socio; en cada ejercicio se usa la cuenta de esta identidad."),
            new("SociosSociedad", "IdIdentidadRetencion", "Subcuenta 4751 (retención de dividendos) del socio; en cada ejercicio se usa la cuenta de esta identidad."),
            new("SociosSociedad", "Activo", "Si es socio de la sociedad; al quitarlo la fila se conserva con sus subcuentas."),
            new("RepartosDividendos", null, "Reparto de dividendos confirmado: su fecha, retención, cuenta de reservas, totales y su asiento (lote contabilidad/procesos/aplicacion-resultado · DIVIDENDOS · IdRepartoDividendos)."),
            new("RepartosDividendos", "IdRepartoDividendos", "Identificador del reparto; es la clave del lote de su asiento."),
            new("RepartosDividendos", "IdSociedadEjercicio", "Ejercicio en que se reparte."),
            new("RepartosDividendos", "Fecha", "Fecha del reparto y de su asiento."),
            new("RepartosDividendos", "PorcentajeRetencion", "Retención aplicada, en porcentaje (19 = 19 %)."),
            new("RepartosDividendos", "CuentaReservas", "Cuenta de reservas que se carga por el bruto."),
            new("RepartosDividendos", "Neto", "Suma de lo que perciben los socios."),
            new("RepartosDividendos", "Bruto", "Suma de los brutos de los socios: lo que sale de reservas."),
            new("RepartosDividendos", "Retencion", "Suma de las retenciones de los socios (a ingresar en Hacienda)."),
            new("RepartosDividendos", "Motivo", "Motivo de la última corrección (vacío en el primer reparto)."),
            new("RepartosDividendos", "IdAsiento", "Asiento del reparto."),
            new("RepartosDividendos", "Operador", "Operador que lo confirmó o corrigió por última vez."),
            new("RepartosDividendos", "FechaHoraConfirmacion", "Fecha y hora de la última confirmación."),
            new("RepartosDividendosSocios", null, "Detalle por socio de un reparto de dividendos."),
            new("RepartosDividendosSocios", "IdRepartoDividendosSocio", "Identificador de la fila."),
            new("RepartosDividendosSocios", "IdRepartoDividendos", "Reparto."),
            new("RepartosDividendosSocios", "IdSocio", "Socio."),
            new("RepartosDividendosSocios", "Participacion", "Participación del socio al repartir, en porcentaje."),
            new("RepartosDividendosSocios", "Neto", "Lo que percibe el socio."),
            new("RepartosDividendosSocios", "Bruto", "Neto / (1 − retención), redondeado al céntimo (al alza en el medio céntimo)."),
            new("RepartosDividendosSocios", "Retencion", "Bruto − neto."),
            new("RepartosDividendosSocios", "CuentaDividendos", "Subcuenta 526 del socio usada en el asiento."),
            new("RepartosDividendosSocios", "CuentaRetencion", "Subcuenta 4751 del socio usada en el asiento."),
            new("SociedadesEjercicios", "PorcentajeRetencionDividendos", "Retención de los dividendos del ejercicio, en porcentaje (vacía: 19 %)."),
            new("SociedadesEjercicios", "CuentaReservasDividendos", "Cuenta de reservas de la que salen los dividendos (vacía: 1130000)."),
        ],
    };
}

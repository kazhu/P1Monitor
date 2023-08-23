namespace P1Monitor.Model;

public record class ObisMapping(string Id, string FieldName, DsmrType P1Type, DsmrUnit Unit = DsmrUnit.None, int Index = 0);
using System.Text.RegularExpressions;

namespace P1Monitor;

public abstract record class P1Value(string FieldName, string Id)
{
	public abstract bool IsValid { get; }
}

public partial record class P1NoValue(string FieldName, string Id, string Value) : P1Value(FieldName, Id)
{
	public static ObisMapping GetMapping(string id, string fieldName) => new ObisMapping(id, fieldName, (value) => new P1NoValue(fieldName, id, value));

	public override bool IsValid => true;
}

public record class P1StringValue(string FieldName, string Id, string Value) : P1Value(FieldName, Id)
{
	public static ObisMapping GetMapping(string id, string fieldName) => new ObisMapping(id, fieldName, (value) => new P1StringValue(fieldName, id, value));

	override public bool IsValid => true;
}

public enum P1Unit
{
	None,
	kWh,
	kvarh,
	kW,
	kvar,
	Hz,
	V,
	A,
}

public static class P1UnitExtensions
{
	public static P1Unit ToP1Unit(this string text) => text switch
	{
		"*kWh" => P1Unit.kWh,
		"*kvarh" => P1Unit.kvarh,
		"*kW" => P1Unit.kW,
		"*kvar" => P1Unit.kvar,
		"*Hz" => P1Unit.Hz,
		"*V" => P1Unit.V,
		"*A" => P1Unit.A,
		"" => P1Unit.None,
		_ => throw new ArgumentException($"Unknown unit {text}", nameof(text)),
	};
}

public partial record class P1NumberValue(string FieldName, string Id, string TextValue) : P1Value(FieldName, Id)
{
	public static ObisMapping GetMapping(string id, string fieldName) => new ObisMapping(id, fieldName, (value) => new P1NumberValue(fieldName, id, value));

	public decimal Value => decimal.Parse(ParseRegex().Match(TextValue).Groups["number"].Value);

	public P1Unit Unit => ParseRegex().Match(TextValue).Groups["unit"].Value.ToP1Unit();

	public override bool IsValid => ParseRegex().IsMatch(TextValue);

	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)(?<unit>\*kWh|\*kvarh|\*kW|\*kvar|\*Hz|\*V|\*A|)\z")]
	private static partial Regex ParseRegex();
}

public partial record class P1TimeValue(string FieldName, string Id, string TextValue) : P1Value(FieldName, Id)
{
	public static ObisMapping GetMapping(string id, string fieldName) => new ObisMapping(id, fieldName, (value) => new P1TimeValue(fieldName, id, value));

	public DateTimeOffset Value => new DateTimeOffset(
		2000 + int.Parse(TextValue.Substring(0, 2)), 
		int.Parse(TextValue.Substring(2, 2)), 
		int.Parse(TextValue.Substring(4, 2)), 
		int.Parse(TextValue.Substring(6, 2)), 
		int.Parse(TextValue.Substring(8, 2)), 
		int.Parse(TextValue.Substring(10, 2)), 
		DateTimeOffset.Now.Offset);
	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	[GeneratedRegex(@"^\d{12}S\z")]
	private static partial Regex ValidatorRegex();
}

public partial record class P1OnOffValue(string FieldName, string Id, string TextValue) : P1StringValue(FieldName, Id, TextValue)
{
	public static ObisMapping GetMapping(string id, string fieldName) => new ObisMapping(id, fieldName, (value) => new P1OnOffValue(fieldName, id, value));

	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public bool IsOn => TextValue == "ON";

	[GeneratedRegex(@"^(?:ON|OFF)\z")]
	private static partial Regex ValidatorRegex();
}


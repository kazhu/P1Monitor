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

public partial record class P1NumberValue(string FieldName, string Id, string TextValue, P1Unit Unit) : P1Value(FieldName, Id)
{
	public static ObisMapping GetMapping(string id, string fieldName, P1Unit expectedUnit) => new ObisMapping(id, fieldName, (value) => new P1NumberValue(fieldName, id, value, expectedUnit));

	public decimal Value => decimal.Parse(ParseRegex().Match(TextValue).Groups["number"].Value);

	public override bool IsValid => ParseRegex().IsMatch(TextValue);

	private Regex ParseRegex() => Unit switch
	{
		P1Unit.kWh => ParseRegex_kWh(),
		P1Unit.kvarh => ParseRegex_kvarh(),
		P1Unit.kW => ParseRegex_kW(),
		P1Unit.kvar => ParseRegex_kvar(),
		P1Unit.Hz => ParseRegex_Hz(),
		P1Unit.V => ParseRegex_V(),
		P1Unit.A => ParseRegex_A(),
		P1Unit.None => ParseRegex_None(),
		_ => throw new ArgumentException($"Unknown unit {Unit}", nameof(Unit)),
	};

	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*kWh\z")] private static partial Regex ParseRegex_kWh();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*kvarh\z")] private static partial Regex ParseRegex_kvarh();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*kW\z")] private static partial Regex ParseRegex_kW();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*kvar\z")] private static partial Regex ParseRegex_kvar();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*Hz\z")] private static partial Regex ParseRegex_Hz();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*V\z")] private static partial Regex ParseRegex_V();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\*A\z")] private static partial Regex ParseRegex_A();
	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)\z")] private static partial Regex ParseRegex_None();
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
	public static new ObisMapping GetMapping(string id, string fieldName) => new ObisMapping(id, fieldName, (value) => new P1OnOffValue(fieldName, id, value));

	public override bool IsValid => ValidatorRegex().IsMatch(TextValue);

	public bool IsOn => TextValue == "ON";

	[GeneratedRegex(@"^(?:ON|OFF)\z")]
	private static partial Regex ValidatorRegex();
}


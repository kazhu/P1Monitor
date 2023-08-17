using System.Text.RegularExpressions;

namespace P1Monitor;

public enum P1Type
{
	NotNeeded,
	String,
	Number,
	Time,
	OnOff,
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

public partial record struct P1Value(string FieldName, string Id, string Data, P1Type P1Type, bool IsValid = true, P1Unit Unit = P1Unit.None, DateTimeOffset? Time = null)
{
	public static ObisMapping GetNotNeededMapping(string id, string fieldName) => 
		new ObisMapping(id, fieldName, (value) => new P1Value(fieldName, id, value, P1Type.NotNeeded));
	public static ObisMapping GetStringMapping(string id, string fieldName) => 
		new ObisMapping(id, fieldName, (value) => new P1Value(fieldName, id, value, P1Type.String));
	public static ObisMapping GetOnOffMapping(string id, string fieldName) =>
		new ObisMapping(id, fieldName, (value) => new P1Value(fieldName, id, value, P1Type.OnOff, value == "ON" || value == "OFF"));
	public static ObisMapping GetNumberMapping(string id, string fieldName, P1Unit unit)
	{
		return new ObisMapping(id, fieldName, (value) =>
		{
			Match match = ParseNumberRegex().Match(value);
			string expectedUnit = unit switch
			{
				P1Unit.kWh => "*kWh",
				P1Unit.kvarh => "*kvarh",
				P1Unit.kW => "*kW",
				P1Unit.kvar => "*kvar",
				P1Unit.Hz => "*Hz",
				P1Unit.V => "*V",
				P1Unit.A => "*A",
				P1Unit.None => "",
				_ => throw new ArgumentException($"Unknown unit {unit}", nameof(unit)),
			};
			bool isValid = match.Success && match.Groups["unit"].Value == expectedUnit;
			return new P1Value(fieldName, id, isValid ? match.Groups["number"].Value : value, P1Type.Number, isValid, Unit: unit);
		});
	}
	public static ObisMapping GetTimeMapping(string id, string fieldName)
	{
		return new ObisMapping(id, fieldName, (value) =>
		{
			bool isValid = value.Length == 13 && value[12] == 'S';
			for (int i = 0; isValid && i < 12; i++) isValid = char.IsDigit(value[i]);
			DateTimeOffset? time = null;
			if (isValid)
			{
				int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
				isValid =
					int.TryParse(value.Substring(0, 2), out year) && year >= 0 && year <= 99 &&
					int.TryParse(value.Substring(2, 2), out month) && month >= 1 && month <= 12 &&
					int.TryParse(value.Substring(4, 2), out day) && day >= 1 && day <= 31 &&
					int.TryParse(value.Substring(6, 2), out hour) && hour >= 0 && hour <= 23 &&
					int.TryParse(value.Substring(8, 2), out minute) && minute >= 0 && minute <= 59 &&
					int.TryParse(value.Substring(10, 2), out second) && second >= 0 && second <= 59;
				if (isValid)
				{
					try
					{
						time = new DateTimeOffset(2000 + year, month, day, hour, minute, second, DateTimeOffset.Now.Offset);
					}
					catch (ArgumentException)
					{
						isValid = false;
					}
				}
			}
			return new P1Value(fieldName, id, value, P1Type.Time, isValid, Time: time);
		});
	}

	[GeneratedRegex(@"^(?<number>\d{1,15}(?:\.\d{1,9})?)(?<unit>|\*.+)\z")]
	private static partial Regex ParseNumberRegex();
}

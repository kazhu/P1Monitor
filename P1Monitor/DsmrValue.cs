using P1Monitor.Model;
using System.Globalization;
using System.Text;

namespace P1Monitor;

public abstract class DsmrValue(ObisMapping mapping)
{
	protected static readonly Encoding _encoding = Encoding.Latin1;
	public static readonly DsmrValue Error = new DsmrErrorValue();

    public static DsmrValue Create(ObisMapping mapping) => mapping.DsmrType switch
	{
		DsmrType.Ignored => new DsmrIgnoredValue(mapping),
		DsmrType.String => new DsmrStringValue(mapping),
		DsmrType.Number => new DsmrNumberValue(mapping),
		DsmrType.Time => new DsmrTimeValue(mapping),
		DsmrType.OnOff => new DsmrOnOffValue(mapping),
		_ => throw new NotImplementedException(),
	};

    public ObisMapping Mapping { get; init; } = mapping;

    public bool IsEmpty { get; protected set; } = true;

	public abstract bool TrySetValue(ReadOnlySpan<byte> span);

	public virtual void Clear() => IsEmpty = true;

	public override string ToString() => IsEmpty ? $"{Mapping.Id} {Mapping.FieldName}: empty" : null!;

	private class DsmrErrorValue : DsmrValue
	{
		public DsmrErrorValue() : base(new ObisMapping("ERROR", "ERROR", DsmrType.Ignored)) { }
		public override bool TrySetValue(ReadOnlySpan<byte> span) => false;
	}
}

public class DsmrIgnoredValue(ObisMapping mapping) : DsmrValue(mapping)
{
    public override bool TrySetValue(ReadOnlySpan<byte> span)
	{
		IsEmpty = false;
		return true;
	}

	public override string ToString() => base.ToString() ?? $"{Mapping.Id} {Mapping.FieldName}: ignored";
	public override bool Equals(object? obj) => obj is DsmrIgnoredValue dsmrIgnored && dsmrIgnored.IsEmpty == IsEmpty && dsmrIgnored.Mapping.Equals(Mapping);
	public override int GetHashCode() => HashCode.Combine(Mapping, IsEmpty);
}

public class DsmrStringValue : DsmrValue
{
	private static readonly DsmrStringInternCache _cache = new(32);
	private string _value = "";

	public DsmrStringValue(ObisMapping mapping) : base(mapping) { }
	public DsmrStringValue(ObisMapping mapping, string value) : base(mapping)
	{
		_value = value;
		IsEmpty = false;
	}

	public string Value => _value;

	public override bool TrySetValue(ReadOnlySpan<byte> span)
	{
		if (span.Length > 32) goto Failed;
		_value = _cache.Get(span);
		IsEmpty = false;
		return true;

	Failed:
		Clear();
		return false;
	}

	override public void Clear()
	{
		base.Clear();
		_value = "";
	}

	public override string ToString() => base.ToString() ?? $"{Mapping.Id} {Mapping.FieldName}: \"{Value}\"";
	public override bool Equals(object? obj) => obj is DsmrStringValue dsmrString && dsmrString.Mapping.Equals(Mapping) && dsmrString.IsEmpty == IsEmpty && dsmrString.Value == Value;
	override public int GetHashCode() => HashCode.Combine(Mapping, IsEmpty, Value);
}

public class DsmrNumberValue : DsmrValue
{
	private decimal _value;

	public DsmrNumberValue(ObisMapping mapping) : base(mapping) { }
	public DsmrNumberValue(ObisMapping mapping, decimal value) : base(mapping)
	{
		_value = value;
		IsEmpty = false;
	}

	public decimal Value => _value;

	public override bool TrySetValue(ReadOnlySpan<byte> span)
	{
		int separatorIndex = span.IndexOf((byte)'*');
		if (separatorIndex >= 0)
		{
			ReadOnlySpan<byte> expectedUnitText = Mapping.Unit switch
			{
				DsmrUnit.kWh => "*kWh"u8,
				DsmrUnit.kvarh => "*kvarh"u8,
				DsmrUnit.kW => "*kW"u8,
				DsmrUnit.kvar => "*kvar"u8,
				DsmrUnit.Hz => "*Hz"u8,
				DsmrUnit.V => "*V"u8,
				DsmrUnit.A => "*A"u8,
				_ => [],
			};
			if (!span[separatorIndex..].SequenceEqual(expectedUnitText)) goto Failed;
			span = span[..separatorIndex];
		}
		else if (Mapping.Unit != DsmrUnit.None) goto Failed;
		if (span.Length > 30) goto Failed;

		// drop trailing zeros
		if (span.IndexOf((byte)'.') > 0)
		{
			int i = span.Length - 1;
			while (i >= 0 && span[i] == '0') i--;
			span = span[..(i + 1)];
		}

		Span<char> charSpan = stackalloc char[span.Length];
		if (_encoding.GetChars(span, charSpan) != charSpan.Length || !decimal.TryParse(charSpan, out _value)) goto Failed;
		IsEmpty = false;
		return true;

	Failed:
		Clear();
		return false;
	}

	override public void Clear()
	{
		base.Clear();
		_value = 0;
	}

	public override string ToString() => base.ToString() ?? (Mapping.Unit == DsmrUnit.None ? $"{Mapping.Id} {Mapping.FieldName}: {Value}" : $"{Mapping.Id} {Mapping.FieldName}: {Value} {Mapping.Unit}");
	public override bool Equals(object? obj) => obj is DsmrNumberValue dsmrNumber && dsmrNumber.Mapping.Equals(Mapping) && dsmrNumber.IsEmpty == IsEmpty && dsmrNumber.Value == Value;
	public override int GetHashCode() => HashCode.Combine(Mapping, IsEmpty, Value);
}

public class DsmrTimeValue : DsmrValue
{
	private DateTimeOffset? _value;

	public DsmrTimeValue(ObisMapping mapping) : base(mapping) { }
	public DsmrTimeValue(ObisMapping mapping, DateTimeOffset value) : base(mapping)
	{
		_value = value;
		IsEmpty = false;
	}

	public DateTimeOffset? Value => _value;

	public override bool TrySetValue(ReadOnlySpan<byte> span)
	{
		if (span.Length != 13 || (span[12] != 'S' && span[12] != 'W')) goto Failed;
		span = span[..12];

		Span<char> charSpan = stackalloc char[span.Length];
		if (Encoding.Latin1.GetChars(span, charSpan) != charSpan.Length || !DateTimeOffset.TryParseExact(charSpan, "yyMMddHHmmss", null, DateTimeStyles.AssumeLocal, out DateTimeOffset value)) goto Failed;
		_value = value;
		IsEmpty = false;
		return true;

	Failed:
		Clear();
		return false;
	}

	override public void Clear()
	{
		base.Clear();
		_value = null;
	}

	public override string ToString() => base.ToString() ?? $"{Mapping.Id} {Mapping.FieldName}: {Value:O}";
	public override bool Equals(object? obj) => obj is DsmrTimeValue dsmrTime && dsmrTime.Mapping.Equals(Mapping) && dsmrTime.IsEmpty == IsEmpty && dsmrTime.Value == Value;
	public override int GetHashCode() => HashCode.Combine(Mapping, IsEmpty, Value);
}

public class DsmrOnOffValue : DsmrValue
{
	public enum OnOff
	{
		OFF = 0,
		ON = 1,
	}

	private OnOff _value;

	public DsmrOnOffValue(ObisMapping mapping) : base(mapping) { }
	public DsmrOnOffValue(ObisMapping mapping, OnOff value) : base(mapping)
	{
		_value = value;
		IsEmpty = false;
	}

	public OnOff Value => _value;

	public override bool TrySetValue(ReadOnlySpan<byte> span)
	{
		if (span.SequenceEqual("ON"u8))
		{
			_value = OnOff.ON;
		}
		else if (span.SequenceEqual("OFF"u8))
		{
			_value = OnOff.OFF;
		}
		else
		{
			Clear();
			return false;
		}
		IsEmpty = false;
		return true;
	}

	public override string ToString() => base.ToString() ?? $"{Mapping.Id} {Mapping.FieldName}: {Value}";
	public override bool Equals(object? obj) => obj is DsmrOnOffValue dsmrOnOff && dsmrOnOff.Mapping.Equals(Mapping) && dsmrOnOff.IsEmpty == IsEmpty && dsmrOnOff.Value == Value;
	public override int GetHashCode() => HashCode.Combine(Mapping, IsEmpty, Value);
}

﻿using System.Buffers;

namespace P1Monitor;

public record class ObisMapping(TrimmedMemory Id, string FieldName, P1Type P1Type, P1Unit Unit = P1Unit.None, int Index = 0)
{
	public static readonly ObisMapping[] Mappings =
		new[]
		{
			new ObisMapping(new TrimmedMemory("0-0:1.0.0"u8), "time", P1Type.Time),
			new ObisMapping(new TrimmedMemory("0-0:42.0.0"u8), "name", P1Type.String),
			new ObisMapping(new TrimmedMemory("0-0:96.1.0"u8), "serial", P1Type.String),
			new ObisMapping(new TrimmedMemory("0-0:96.14.0"u8), "tariff", P1Type.Number, P1Unit.None),
			new ObisMapping(new TrimmedMemory("0-0:96.50.68"u8), "state", P1Type.OnOff),
			new ObisMapping(new TrimmedMemory("1-0:1.8.0"u8), "import_energy", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:1.8.1"u8), "import_energy_tariff_1", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:1.8.2"u8), "import_energy_tariff_2", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:1.8.3"u8), "import_energy_tariff_3", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:1.8.4"u8), "import_energy_tariff_4", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:2.8.0"u8), "export_energy", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:2.8.1"u8), "export_energy_tariff_1", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:2.8.2"u8), "export_energy_tariff_2", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:2.8.3"u8), "export_energy_tariff_3", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:2.8.4"u8), "export_energy_tariff_4", P1Type.Number, P1Unit.kWh),
			new ObisMapping(new TrimmedMemory("1-0:3.8.0"u8), "import_reactive_energy", P1Type.Number, P1Unit.kvarh),
			new ObisMapping(new TrimmedMemory("1-0:4.8.0"u8), "export_reactive_energy", P1Type.Number, P1Unit.kvarh),
			new ObisMapping(new TrimmedMemory("1-0:5.8.0"u8), "reactive_energy_q1", P1Type.Number, P1Unit.kvarh),
			new ObisMapping(new TrimmedMemory("1-0:6.8.0"u8), "reactive_energy_q2", P1Type.Number, P1Unit.kvarh),
			new ObisMapping(new TrimmedMemory("1-0:7.8.0"u8), "reactive_energy_q3", P1Type.Number, P1Unit.kvarh),
			new ObisMapping(new TrimmedMemory("1-0:8.8.0"u8), "reactive_energy_q4", P1Type.Number, P1Unit.kvarh),
			new ObisMapping(new TrimmedMemory("1-0:32.7.0"u8), "voltage_l1", P1Type.Number, P1Unit.V),
			new ObisMapping(new TrimmedMemory("1-0:52.7.0"u8), "voltage_l2", P1Type.Number, P1Unit.V),
			new ObisMapping(new TrimmedMemory("1-0:72.7.0"u8), "voltage_l3", P1Type.Number, P1Unit.V),
			new ObisMapping(new TrimmedMemory("1-0:31.7.0"u8), "current_l1", P1Type.Number, P1Unit.A),
			new ObisMapping(new TrimmedMemory("1-0:51.7.0"u8), "current_l2", P1Type.Number, P1Unit.A),
			new ObisMapping(new TrimmedMemory("1-0:71.7.0"u8), "current_l3", P1Type.Number, P1Unit.A),
			new ObisMapping(new TrimmedMemory("1-0:13.7.0"u8), "power_factor", P1Type.Number, P1Unit.None),
			new ObisMapping(new TrimmedMemory("1-0:33.7.0"u8), "power_factor_l1", P1Type.Number, P1Unit.None),
			new ObisMapping(new TrimmedMemory("1-0:53.7.0"u8), "power_factor_l2", P1Type.Number, P1Unit.None),
			new ObisMapping(new TrimmedMemory("1-0:73.7.0"u8), "power_factor_l3", P1Type.Number, P1Unit.None),
			new ObisMapping(new TrimmedMemory("1-0:14.7.0"u8), "frequency", P1Type.Number, P1Unit.Hz),
			new ObisMapping(new TrimmedMemory("1-0:1.7.0"u8), "import_power", P1Type.Number, P1Unit.kW),
			new ObisMapping(new TrimmedMemory("1-0:2.7.0"u8), "export_power", P1Type.Number, P1Unit.kW),
			new ObisMapping(new TrimmedMemory("1-0:5.7.0"u8), "reactive_power_q1", P1Type.Number, P1Unit.kvar),
			new ObisMapping(new TrimmedMemory("1-0:6.7.0"u8), "reactive_power_q2", P1Type.Number, P1Unit.kvar),
			new ObisMapping(new TrimmedMemory("1-0:7.7.0"u8), "reactive_power_q3", P1Type.Number, P1Unit.kvar),
			new ObisMapping(new TrimmedMemory("1-0:8.7.0"u8), "reactive_power_q4", P1Type.Number, P1Unit.kvar),
			new ObisMapping(new TrimmedMemory("0-0:17.0.0"u8), "limiter_limit", P1Type.NotNeeded),
			new ObisMapping(new TrimmedMemory("1-0:15.8.0"u8), "energy_combined", P1Type.NotNeeded),
			new ObisMapping(new TrimmedMemory("1-0:31.4.0"u8), "current_limit_l1", P1Type.NotNeeded),
			new ObisMapping(new TrimmedMemory("1-0:51.4.0"u8), "current_limit_l2", P1Type.NotNeeded),
			new ObisMapping(new TrimmedMemory("1-0:71.4.0"u8), "current_limit_l3", P1Type.NotNeeded),
			new ObisMapping(new TrimmedMemory("0-0:98.1.0"u8), "previous_month", P1Type.NotNeeded),
			new ObisMapping(new TrimmedMemory("0-0:96.13.0"u8), "message", P1Type.NotNeeded),
		}
		.Select((x, i) => x with { Index = i })
		.ToArray();

	public static readonly Dictionary<TrimmedMemory, ObisMapping> MappingById = Mappings.ToDictionary(x => x.Id);
	public static readonly Dictionary<string, ObisMapping> MappingByFieldName = Mappings.ToDictionary(x => x.FieldName);
	public static readonly ObisMapping[] Tags = Mappings.Where(x => x.P1Type == P1Type.String || x.P1Type == P1Type.OnOff).OrderBy(x => x.FieldName).ToArray();
	public static readonly Dictionary<string, ObisMapping[]> NumberMappingsByUnit = Mappings.Where(x => x.P1Type == P1Type.Number).GroupBy(x => x.Unit).ToDictionary(g => g.Key.ToString(), g => g.OrderBy(x => x.FieldName).ToArray());
}
using P1Monitor.Model;

namespace P1Monitor.Tests;

public class TestObisMappingsProvider : IObisMappingsProvider
{
	private static readonly ObisMapping[] TestMappings =
		new[]
		{
			new ObisMapping("0-0:1.0.0", "time", DsmrType.Time),
			new ObisMapping("0-0:42.0.0", "name", DsmrType.String),
			new ObisMapping("0-0:96.1.0", "serial", DsmrType.String),
			new ObisMapping("0-0:96.14.0", "tariff", DsmrType.Number, DsmrUnit.None),
			new ObisMapping("0-0:96.50.68", "state", DsmrType.OnOff),
			new ObisMapping("1-0:1.8.0", "import_energy", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:1.8.1", "import_energy_tariff_1", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:1.8.2", "import_energy_tariff_2", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:1.8.3", "import_energy_tariff_3", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:1.8.4", "import_energy_tariff_4", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:2.8.0", "export_energy", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:2.8.1", "export_energy_tariff_1", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:2.8.2", "export_energy_tariff_2", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:2.8.3", "export_energy_tariff_3", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:2.8.4", "export_energy_tariff_4", DsmrType.Number, DsmrUnit.kWh),
			new ObisMapping("1-0:3.8.0", "import_reactive_energy", DsmrType.Number, DsmrUnit.kvarh),
			new ObisMapping("1-0:4.8.0", "export_reactive_energy", DsmrType.Number, DsmrUnit.kvarh),
			new ObisMapping("1-0:5.8.0", "reactive_energy_q1", DsmrType.Number, DsmrUnit.kvarh),
			new ObisMapping("1-0:6.8.0", "reactive_energy_q2", DsmrType.Number, DsmrUnit.kvarh),
			new ObisMapping("1-0:7.8.0", "reactive_energy_q3", DsmrType.Number, DsmrUnit.kvarh),
			new ObisMapping("1-0:8.8.0", "reactive_energy_q4", DsmrType.Number, DsmrUnit.kvarh),
			new ObisMapping("1-0:32.7.0", "voltage_l1", DsmrType.Number, DsmrUnit.V),
			new ObisMapping("1-0:52.7.0", "voltage_l2", DsmrType.Number, DsmrUnit.V),
			new ObisMapping("1-0:72.7.0", "voltage_l3", DsmrType.Number, DsmrUnit.V),
			new ObisMapping("1-0:31.7.0", "current_l1", DsmrType.Number, DsmrUnit.A),
			new ObisMapping("1-0:51.7.0", "current_l2", DsmrType.Number, DsmrUnit.A),
			new ObisMapping("1-0:71.7.0", "current_l3", DsmrType.Number, DsmrUnit.A),
			new ObisMapping("1-0:13.7.0", "power_factor", DsmrType.Number, DsmrUnit.None),
			new ObisMapping("1-0:33.7.0", "power_factor_l1", DsmrType.Number, DsmrUnit.None),
			new ObisMapping("1-0:53.7.0", "power_factor_l2", DsmrType.Number, DsmrUnit.None),
			new ObisMapping("1-0:73.7.0", "power_factor_l3", DsmrType.Number, DsmrUnit.None),
			new ObisMapping("1-0:14.7.0", "frequency", DsmrType.Number, DsmrUnit.Hz),
			new ObisMapping("1-0:1.7.0", "import_power", DsmrType.Number, DsmrUnit.kW),
			new ObisMapping("1-0:2.7.0", "export_power", DsmrType.Number, DsmrUnit.kW),
			new ObisMapping("1-0:5.7.0", "reactive_power_q1", DsmrType.Number, DsmrUnit.kvar),
			new ObisMapping("1-0:6.7.0", "reactive_power_q2", DsmrType.Number, DsmrUnit.kvar),
			new ObisMapping("1-0:7.7.0", "reactive_power_q3", DsmrType.Number, DsmrUnit.kvar),
			new ObisMapping("1-0:8.7.0", "reactive_power_q4", DsmrType.Number, DsmrUnit.kvar),
			new ObisMapping("0-0:17.0.0", "limiter_limit", DsmrType.Ignored),
			new ObisMapping("1-0:15.8.0", "energy_combined", DsmrType.Ignored),
			new ObisMapping("1-0:31.4.0", "current_limit_l1", DsmrType.Ignored),
			new ObisMapping("1-0:51.4.0", "current_limit_l2", DsmrType.Ignored),
			new ObisMapping("1-0:71.4.0", "current_limit_l3", DsmrType.Ignored),
			new ObisMapping("0-0:98.1.0", "previous_month", DsmrType.Ignored),
			new ObisMapping("0-0:96.13.0", "message", DsmrType.Ignored),
		}
		.Select((x, i) => x with { Index = i })
		.ToArray();

	public IObisMappings Mappings { get; } = new ObisMappings(TestMappings);
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meadow;
using Meadow.Devices;
using Meadow.Foundation;
using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors.Atmospheric;
using Meadow.Peripherals.Sensors.Atmospheric;

namespace Sensors.Atmospheric.Bme680_Sample
{
    public class MeadowApp : App<F7Micro, MeadowApp>
    {
        Bme680 bme680;

        public MeadowApp()
        {
            Initialize();

            Run();

            //IAQ example
            //Task.Factory.StartNew(async () =>
            //{
            //    await IndoorAirQuality();
            //});

        }

        void Initialize()
        {
            Console.WriteLine("Initialize hardware...");

            // configure our BME280 on the I2C Bus
            var i2c = Device.CreateI2cBus();
            bme680 = new Bme680(
                i2c,
                Bme680.I2cAddress.Adddress0x76
            );

            bme680.Initialize();

            // TODO: SPI version

            // Example that uses an IObersvable subscription to only be notified
            // when the temperature changes by at least a degree, and humidty by 5%.
            // (blowing hot breath on the sensor should trigger)
            //bme680.Subscribe(new FilterableObserver<AtmosphericConditionPlusGasChangeResult, AtmosphericPlusGasConditions>(
            //    h =>
            //    {
            //        Console.WriteLine($"Temp or pressure changed by threshold; new temp: {h.New.Temperature}, old: {h.Old.Temperature}");
            //    },
            //    e =>
            //    {
            //        return (
            //            (Math.Abs(e.Delta.Temperature.Value) > 1)
            //            ||
            //            (Math.Abs(e.Delta.Pressure.Value) > 5)
            //            );
            //    }
            //    ));

            // classical .NET events can also be used:
            bme680.Updated += (object sender, AtmosphericConditionPlusGasChangeResult e) =>
            {
                Console.WriteLine($"Temperature: {e.New.Temperature:F} C");
                Console.WriteLine($"Pressure: {e.New.Pressure:F}hPa");
                Console.WriteLine($"Relative Humidity: {e.New.Humidity:F}%");
                Console.WriteLine($"Gas Resistance: {e.New.GasResistance:F}ohms");
                Console.WriteLine($"Altitude: {e.New.Humidity:F}m");
            };

        }

        void Run()
        {
            Console.WriteLine("Run...");

            // just for funsies.
            Console.WriteLine($"ChipID: {bme680.ChipId:X2}");

            ReadConditions().Wait();

            // start updating continuously
            bme680.StartUpdating();

        }

        protected async Task ReadConditions()
        {
            var conditions = await bme680.ReadAsync();
            Console.WriteLine("Initial Readings:");
            Console.WriteLine($"  Temperature: {conditions.Temperature:F}°C");
            Console.WriteLine($"  Pressure: {conditions.Pressure:F}hPa");
            Console.WriteLine($"  Relative Humidity: {conditions.Humidity:F}%");
            Console.WriteLine($"  Gas Resistance: {conditions.GasResistance}ohms");
            Console.WriteLine($"  Altitude: {conditions.Altitude:F}m");
            Console.WriteLine($"");
        }

        protected async Task IndoorAirQuality()
        {
            Console.WriteLine("Indoor Air Quality:");
            DateTime burnInEndTime = DateTime.Now.AddMinutes(5);
            float[] gasData = new float[50];
            int index=0;

            Console.WriteLine("Collecting gas resistance burn-in data for 5 mins");
            Console.WriteLine($"StartTime: {DateTime.Now.ToString()} EndTime: {burnInEndTime.ToString()}");
            while (burnInEndTime > DateTime.Now)
            {
                var conditions = await bme680.ReadAsync();
                Thread.Sleep(10);
                if (conditions.GasResistance.HasValue)
                {
                    gasData[index] = conditions.GasResistance.Value;
                    index++;

                    if (index >= gasData.Length)
                    {
                        Console.WriteLine($"{DateTime.Now.ToString()} gas: { conditions.GasResistance}");
                        index = 0;
                    }
                }
            }

            Console.WriteLine("burn-in completed");
            var gasBaseline = gasData.Sum() / 50.0f;

            Console.WriteLine($"gas baseline: { gasBaseline}");

            while (true)
            {
                var conditions = await bme680.ReadAsync();
                
                if (conditions.GasResistance == null || conditions.Humidity == null)
                    continue;

                var iaq = Bme680.IAQIndex(gasBaseline, conditions.GasResistance.Value, conditions.Humidity.Value);
                Console.WriteLine($"IAQ: { iaq:F}, Gas: { conditions.GasResistance} ohms, Humidity: { conditions.Humidity:F}%");

                Thread.Sleep(500);
            }
        }
    }
}

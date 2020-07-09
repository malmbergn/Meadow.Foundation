using System;
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
            //bme680.Subscribe(new FilterableObserver<AtmosphericConditionChangeResult, AtmosphericConditions>(
            //    h => {
            //        Console.WriteLine($"Temp and pressure changed by threshold; new temp: {h.New.Temperature}, old: {h.Old.Temperature}");
            //    },
            //    e => {
            //        return (
            //            (Math.Abs(e.Delta.Temperature.Value) > 1)
            //            &&
            //            (Math.Abs(e.Delta.Pressure.Value) > 5)
            //            );
            //    }
            //    ));

            // classical .NET events can also be used:
            //bme680.Updated += (object sender, AtmosphericConditionChangeResult e) => {
            //    Console.WriteLine($"Temperature: {e.New.Temperature:F} C");
            //    Console.WriteLine($"Pressure: {e.New.Pressure:F}hPa");
            //    Console.WriteLine($"Relative Humidity: {e.New.Humidity:F}%");
            //};

        }

        void Run()
        {
            Console.WriteLine("Run...");

            // just for funsies.
            Console.WriteLine($"ChipID: {bme680.ChipId:X2}");

            while (true)
            {
                // get an initial reading
                ReadConditions().Wait();
                Thread.Sleep(3000);
                Console.WriteLine("");
            }
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

    }
}

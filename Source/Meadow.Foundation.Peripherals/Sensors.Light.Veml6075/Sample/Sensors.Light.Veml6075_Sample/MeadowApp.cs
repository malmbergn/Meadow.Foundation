using System;
using System.Threading;
using Meadow;
using Meadow.Devices;
using Meadow.Foundation;
using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors.Light;

namespace Sensors.Light.Veml6075_Sample
{
    public class MeadowApp : App<F7Micro, MeadowApp>
    {
        VEML6075 veml6075;

        public MeadowApp()
        {
            Initialize();
            Run();
        }

        void Initialize()
        {
            Console.WriteLine("Initialize hardware...");
            var i2c = Device.CreateI2cBus();
            veml6075 = new VEML6075(i2c);

            //veml6075.Subscribe(new FilterableChangeObserver<UVConditionChangeResult, UVConditions>(
            //h =>
            //{
            //    Console.WriteLine($"UVA or UVB changed by threshold; new UVA: {h.New.UVA}, old: {h.Old.UVA}");
            //},
            //e =>
            //{
            //    return (
            //        (Math.Abs(e.Delta.UVA) > 1)
            //        &&
            //        (Math.Abs(e.Delta.UVB) > 5)
            //        );
            //}
            //));

            // classical .NET events can also be used:
            veml6075.Updated += (object sender, UVConditionChangeResult e) => {
                Console.WriteLine($"UVA: {e.New.UVA}, UVB: {e.New.UVB}, UV Index: {e.New.UVIndex}");
            };

        }

        void Run()
        {
            Console.WriteLine("Run...");

            var reading = veml6075.Reading();
            Console.WriteLine($"UVA: {reading.UVA}, UVB: {reading.UVB}, UV Index: {reading.UVIndex}");
            Thread.Sleep(500);

            Console.WriteLine("Start Updating...");
            veml6075.StartUpdating();
        }
    }
}

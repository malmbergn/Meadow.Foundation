using System;
using System.Threading;
using Meadow;
using Meadow.Devices;
using Meadow.Foundation;
using Meadow.Foundation.Leds;
using Meadow.Foundation.Sensors.Light;

namespace Sensors.Light.Vcnl4010_Sample
{
    public class MeadowApp : App<F7Micro, MeadowApp>
    {
        VCNL4010 vcnl4010;

        public MeadowApp()
        {
            Initialize();
            Run();
        }

        void Initialize()
        {
            Console.WriteLine("Initialize hardware...");
            var i2c = Device.CreateI2cBus();
            vcnl4010 = new VCNL4010(i2c);

            vcnl4010.Subscribe(new FilterableObserver<Vcnl4010ConditionChangeResult, Vcnl4010Conditions>(
            h =>
            {
                Console.WriteLine($"Threshold changed; new Ambient: {h.New.Ambient}, old: {h.Old.Ambient}, Delta:{h.Delta.Ambient} new Prox: {h.New.Proximity} old: {h.Old.Proximity} Delta:{h.Delta.Proximity}");
            },
            e =>
            {
                return (
                    (Math.Abs(e.Delta.Ambient.Value) > 100)
                    ||
                    (Math.Abs(e.Delta.Proximity.Value) > 100)
                    );
            }
            ));

            // classical .NET events can also be used:
            //vcnl4010.Updated += (object sender, Vcnl4010ConditionChangeResult e) =>
            //{
            //    Console.WriteLine($"  Ambient: {e.New.Ambient}");
            //    Console.WriteLine($"  Lux: {e.New.Lux}");
            //    Console.WriteLine($"  Proximity: {e.New.Proximity}");
            //};

        }

        void Run()
        {
            Console.WriteLine("Run...");
            Console.WriteLine($"Id: {vcnl4010.Id}");

            var ambient = vcnl4010.Ambient();
            Console.WriteLine($"Ambient: {ambient}");
            Thread.Sleep(100);

            var lux = vcnl4010.AmbientLux();
            Console.WriteLine($"Lux: {lux}");
            Thread.Sleep(100);

            var prox = vcnl4010.Proximity();
            Console.WriteLine($"Proximity: {prox}");
            Thread.Sleep(100);

            vcnl4010.StartUpdating();
        }
    }
}

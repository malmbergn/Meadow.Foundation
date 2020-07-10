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

        }

        void Run()
        {
            Console.WriteLine("Run...");
            Console.WriteLine($"Id: {vcnl4010.Id}");

            while (true)
            {
                var ambient = vcnl4010.Ambient();
                Console.WriteLine($"Ambient: {ambient}");
                Thread.Sleep(1000);

                var lux = vcnl4010.AmbientLux();
                Console.WriteLine($"Lux: {lux}");
                Thread.Sleep(1000);

                var prox = vcnl4010.Proximity();
                Console.WriteLine($"Proximity: {prox}");
                Thread.Sleep(1000);
            }
        }
    }
}

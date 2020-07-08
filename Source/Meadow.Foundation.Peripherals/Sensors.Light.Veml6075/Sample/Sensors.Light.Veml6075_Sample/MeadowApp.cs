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
            veml6075.Initialize();
        }

        void Run()
        {
            Console.WriteLine("Run...");

            while (true)
            {
                var reading = veml6075.Reading();
                Console.WriteLine($"UVA: {reading.UVA}, UVB: {reading.UVB}, UV Index: {reading.UVIndex}");
                Thread.Sleep(1000);
            }

        }
    }
}

﻿using Meadow.Hardware;
using Meadow.Peripherals.Sensors;
using Meadow.Peripherals.Sensors.Atmospheric;
using Meadow.Units;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    /// <summary>
    /// Provide a mechanism for reading the temperature and humidity from
    /// a SHT31D temperature / humidity sensor.
    /// </summary>
    /// <remarks>
    /// Readings from the sensor are made in Single-shot mode.
    /// </remarks>
    public class Sht31d :
        FilterableChangeObservable<CompositeChangeResult<Units.Temperature, RelativeHumidity>, Units.Temperature, RelativeHumidity>,
        ITemperatureSensor, IHumiditySensor
    {
        /// <summary>
        ///     SH31D sensor communicates using I2C.
        /// </summary>
        private readonly II2cPeripheral sht31d;

        /// <summary>
        /// The temperature, in degrees celsius (°C), from the last reading.
        /// </summary>
        public Units.Temperature Temperature => Conditions.Temperature;

        /// <summary>
        /// The humidity, in percent relative humidity, from the last reading..
        /// </summary>
        public RelativeHumidity Humidity => Conditions.Humidity;

        /// <summary>
        /// The AtmosphericConditions from the last reading.
        /// </summary>
        public (Units.Temperature Temperature, RelativeHumidity Humidity) Conditions;

        // internal thread lock
        private object _lock = new object();
        private CancellationTokenSource SamplingTokenSource;

        /// <summary>
        /// Gets a value indicating whether the analog input port is currently
        /// sampling the ADC. Call StartSampling() to spin up the sampling process.
        /// </summary>
        /// <value><c>true</c> if sampling; otherwise, <c>false</c>.</value>
        public bool IsSampling { get; protected set; } = false;

        public event EventHandler<CompositeChangeResult<Units.Temperature, RelativeHumidity>> Updated;
        public event EventHandler<CompositeChangeResult<Units.Temperature>> TemperatureUpdated;
        public event EventHandler<CompositeChangeResult<RelativeHumidity>> HumidityUpdated;

        /// <summary>
        ///     Create a new SHT31D object.
        /// </summary>
        /// <param name="address">Sensor address (should be 0x44 or 0x45).</param>
        /// <param name="i2cBus">I2cBus (0-1000 KHz).</param>
        public Sht31d(II2cBus i2cBus, byte address = 0x44)
        {
            sht31d = new I2cPeripheral(i2cBus, address);
        }

        /// <summary>
        /// Convenience method to get the current sensor readings. For frequent reads, use
        /// StartSampling() and StopSampling() in conjunction with the SampleBuffer.
        /// </summary>
        public Task<(Units.Temperature Temperature, RelativeHumidity Humidity)> Read()
        {
            Update();

            return Task.FromResult(Conditions);
        }

        public void StartUpdating(int standbyDuration = 1000)
        {
            // thread safety
            lock (_lock) {
                if (IsSampling) { return; }

                // state muh-cheen
                IsSampling = true;

                SamplingTokenSource = new CancellationTokenSource();
                CancellationToken ct = SamplingTokenSource.Token;

                (Units.Temperature, RelativeHumidity) oldConditions;
                CompositeChangeResult<Units.Temperature, RelativeHumidity> result;

                Task.Factory.StartNew(async () => {
                    while (true) {
                        if (ct.IsCancellationRequested) {
                            // do task clean up here
                            observers.ForEach(x => x.OnCompleted());
                            break;
                        }
                        // capture history
                        oldConditions = Conditions;

                        // read
                        Update();

                        // build a new result with the old and new conditions
                        result = new CompositeChangeResult<Units.Temperature, RelativeHumidity>(oldConditions, Conditions);

                        // let everyone know
                        RaiseChangedAndNotify(result);

                        // sleep for the appropriate interval
                        await Task.Delay(standbyDuration);
                    }
                }, SamplingTokenSource.Token);
            }
        }

        protected void RaiseChangedAndNotify(CompositeChangeResult<Units.Temperature, RelativeHumidity> changeResult)
        {
            Updated?.Invoke(this, changeResult);
            HumidityUpdated?.Invoke(this, new CompositeChangeResult<RelativeHumidity>(changeResult.New.Value.Unit2, changeResult.Old.Value.Unit2));
            TemperatureUpdated?.Invoke(this, new CompositeChangeResult<Units.Temperature>(changeResult.New.Value.Unit1, changeResult.Old.Value.Unit1));
            base.NotifyObservers(changeResult);
        }

        /// <summary>
        /// Stops sampling the temperature.
        /// </summary>
        public void StopUpdating()
        {
            lock (_lock) {
                if (!IsSampling) { return; }

                SamplingTokenSource?.Cancel();

                // state muh-cheen
                IsSampling = false;
            }
        }

        /// <summary>
        ///     Get a reading from the sensor and set the Temperature and Humidity properties.
        /// </summary>
        public void Update()
        {
            var data = sht31d.WriteRead(new byte[] { 0x2c, 0x06 }, 6);
            var humidity = (100 * (float)((data[3] << 8) + data[4])) / 65535;
            var tempC = ((175 * (float)((data[0] << 8) + data[1])) / 65535) - 45;

            Conditions.Humidity = new RelativeHumidity(humidity);
            Conditions.Temperature = new Units.Temperature(tempC, Units.Temperature.UnitType.Celsius);
        }
    }
}
using Meadow.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Light
{
    /// <summary>
    /// Represents the VCNL4010 Light/Proximity sensor
    /// </summary>
    /// <remarks>
    /// Default I2C Address is 0x13
    /// </remarks>
    public class VCNL4010 : FilterableObservableBase<Vcnl4010ConditionChangeResult, Vcnl4010Conditions>
    {
        /// <summary>
        /// Proximity measurements Frequency inmeasurements/sec
        /// </summary>
        public enum Frequency : byte
        {
            _1_95 = 0x00,    // 1.95
            _3_90625 = 0x01, // 3.90625
            _7_8125 = 0x02,  // 7.8125
            _16_625 = 0x03,  // 16.625
            _31_25 = 0x04,   // 31.25
            _62_5 = 0x05,    // 62.5
            _125 = 0x06,     // 125
            _250 = 0x07,     // 250
        }

        /// <summary>
        /// The Id of the VCNL4010
        /// </summary>
        public byte Id { get; set; }

        public bool IsSampling { get; protected set; } = false;

        protected const byte DefaultAddress = 0x13;
        protected const byte CommandRegister = 0x80;
        protected const byte ProductIdRegister = 0x81;
        protected const byte ProxRateRegister = 0x82;
        protected const byte IrLedRegister = 0x83;
        protected const byte AmbientParameterRegister = 0x84;
        protected const byte AmbientDataRegister = 0x85;
        protected const byte ProximityDataRegister = 0x87;
        protected const byte InterruptControlRegister = 0x89;
        protected const byte ProximityAdjustRegister = 0x8A;
        protected const byte IntStatRegister = 0x8E;
        protected const byte ModeTimingRegister = 0x8F;
        protected const byte MeasureAmbientCommand = 0x10;
        protected const byte MeasureProximityCommand = 0x08;
        protected const byte AmbientReadyRegister = 0x40;
        protected const byte ProximityReadyRegister = 0x20;
        protected const float AmbientLuxScale = 0.25f;

        protected readonly II2cPeripheral i2CPeripheral;

        protected Vcnl4010Conditions Conditions { get; set; }

        public event EventHandler<Vcnl4010ConditionChangeResult> Updated = delegate { };

        private object _lock = new object();
        private CancellationTokenSource SamplingTokenSource;

        public VCNL4010(II2cBus i2CBus)
        {
            i2CPeripheral = new I2cPeripheral(i2CBus, DefaultAddress);

            Initialize();
        }

        protected virtual void Initialize()
        {
            Id = i2CPeripheral.ReadRegister(ProductIdRegister);

            if ((Id & 0xF0) != 0x20)
                throw new Exception("Failed to find VCNL4010, check wiring!");

            SetLedCurrent(200);
            SetFrequency(Frequency._16_625); // 16.625 readings/second

            i2CPeripheral.WriteRegister(InterruptControlRegister, 0x08);
        }

        /// <summary>
        /// Set the LED current value for proximity measurement.
        /// </summary>
        /// <param name="value">Values from 0-200.
        /// The value is adjustable in steps of 10 mA from 0 mA to 200 mA.
        /// </param>
        public void SetLedCurrent(byte value)
        {
            if (value < 0)
                value = 0;

            if (value > 200)
                value = 200;

            byte intValue = (byte)(value / 10);
            intValue *= 10;

            i2CPeripheral.WriteRegister(IrLedRegister, intValue);

        }

        /// <summary>
        /// Gets the LED current
        /// </summary>
        /// <returns></returns>
        public byte GetLedCurrent()
        {
            return (byte)(i2CPeripheral.ReadRegister(ProxRateRegister) & 0x3F);
        }

        /// <summary>
        /// The frequency of proximity measurements
        /// </summary>
        /// <param name="frequency">The Frequency</param>
        public void SetFrequency(Frequency frequency)
        {
            i2CPeripheral.WriteRegister(ProxRateRegister, (byte)frequency);
        }

        /// <summary>
        /// Gets the current set Frequency
        /// </summary>
        /// <returns></returns>
        public byte GetFrequency()
        {
            return (byte)((i2CPeripheral.ReadRegister(ModeTimingRegister) >> 3) & 0x03);
        }

        /// <summary>
        /// Get the Proximity value
        /// </summary>
        /// <returns>Unit-less unsigned 16-bit value (0-65535)</returns>
        public ushort Proximity()
        {
            var status = i2CPeripheral.ReadRegister(IntStatRegister);
            status = (byte)(status & 0b11100111);

            i2CPeripheral.WriteRegister(IntStatRegister, status);

            i2CPeripheral.WriteRegister(CommandRegister, MeasureProximityCommand);

            for (int i = 0; i < 50; i++)
            {
                byte result = i2CPeripheral.ReadRegister(CommandRegister);
                if ((result & ProximityReadyRegister) == ProximityReadyRegister)
                    return i2CPeripheral.ReadUShort(ProximityDataRegister, Meadow.ByteOrder.BigEndian);

                Thread.Sleep(1);
            }

            return ushort.MaxValue;
        }

        /// <summary>
        /// The detected ambient light in front of the sensor
        /// </summary>
        /// <returns>Unit-less unsigned 16-bit value (0-65535)</returns>
        public ushort Ambient()
        {
            var status = i2CPeripheral.ReadRegister(IntStatRegister);
            status = (byte)(status & 0b11100111);

            i2CPeripheral.WriteRegister(IntStatRegister, status);

            i2CPeripheral.WriteRegister(CommandRegister, MeasureAmbientCommand);

            for (int i = 0; i < 50; i++)
            {
                byte result = i2CPeripheral.ReadRegister(CommandRegister);
                if ((result & AmbientReadyRegister) == AmbientReadyRegister)
                    return i2CPeripheral.ReadUShort(AmbientDataRegister, Meadow.ByteOrder.BigEndian);

                Thread.Sleep(1);
            }

            return ushort.MaxValue;
        }

        /// <summary>
        /// The detected ambient light in front of the sensor as a value in lux
        /// </summary>
        /// <returns></returns>
        public virtual float AmbientLux()
        {
            return Ambient() * AmbientLuxScale;
        }

        /// <summary>
        /// Start Sampling the sensor every X seconds and report the results via the eventing system
        /// </summary>
        /// <param name="enableAmbient">True = Get Ambient/Lux measurement. False = Skip</param>
        /// <param name="enableProximity">True = Get Proximity measurement. False = Skip</param>
        /// <param name="standbyDuration">Polling duration. >= 0</param>
        public void StartUpdating(bool enableAmbient = true, bool enableProximity = true, int standbyDuration = 1000)
        {
            if (standbyDuration <= 0)
                throw new ArgumentException("standbyDuration has to be >= 0", nameof(standbyDuration));

            // thread safety
            lock (_lock)
            {
                if (IsSampling) return;

                // state muh-cheen
                IsSampling = true;

                SamplingTokenSource = new CancellationTokenSource();
                CancellationToken ct = SamplingTokenSource.Token;

                Vcnl4010Conditions oldConditions;
                Vcnl4010ConditionChangeResult result;

                

                Task.Factory.StartNew(async () => {
                    
                    Conditions = await ReadAsync(enableAmbient, enableProximity);
                    await Task.Delay(50);

                    while (true)
                    {

                        if (ct.IsCancellationRequested)
                        {

                            // do task clean up here
                            _observers.ForEach(x => x.OnCompleted());
                            break;
                        }

                        // capture history
                        oldConditions = Vcnl4010Conditions.From(Conditions);

                        // read
                        Conditions = await ReadAsync(enableAmbient, enableProximity);

                        // build a new result with the old and new conditions
                        result = new Vcnl4010ConditionChangeResult(Conditions, oldConditions);

                        // let everyone know
                        RaiseChangedAndNotify(result);

                        // sleep for the appropriate interval
                        await Task.Delay(standbyDuration);
                    }
                }, SamplingTokenSource.Token);
            }
        }

        /// <summary>
        /// Stop polling the sensor
        /// </summary>
        public void StopUpdating()
        {
            lock (_lock)
            {
                if (!IsSampling) return;

                SamplingTokenSource?.Cancel();

                // state muh-cheen
                IsSampling = false;
            }
        }

        protected async Task<Vcnl4010Conditions> ReadAsync(bool enableAmbient, bool enableProximity)
        {
            return await Task.Run(() =>
            {
                Vcnl4010Conditions conditions = new Vcnl4010Conditions();
                if (enableAmbient)
                {
                    conditions.Ambient = Ambient();
                    conditions.Lux = conditions.Ambient * AmbientLuxScale;
                }

                if(enableProximity)
                    conditions.Proximity = Proximity();
                
                return conditions;
            });
        }

        protected void RaiseChangedAndNotify(Vcnl4010ConditionChangeResult changeResult)
        {
            Updated?.Invoke(this, changeResult);
            base.NotifyObservers(changeResult);
        }
    }
}

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
    /// Represents the VEML6075 UV sensor
    /// </summary>
    public class VEML6075 : FilterableObservableBase<UVConditionChangeResult, UVConditions>
    {

        public enum IntegrationtTme : byte
        {
            _50MS = 0x00,
            _100MS = 0x01,
            _200MS = 0x02,
            _400MS = 0x03,
            _800MS = 0x04
        }

        //# Registers/etc.
        protected const byte IdRegister = 0x0C;
        protected const byte ConfigRegister = 0x00;
        protected const byte UvaRegister = 0x07;
        protected const byte DarkRegister = 0x08;
        protected const byte UvbRegister = 0x09;
        protected const byte UvComp1Register = 0x0A;
        protected const byte UvComp2Register = 0x0B;
        protected const byte busAddress = 0x10;

        readonly II2cPeripheral _i2CPeripheral;

        //Coefficient defaults no coverglass
        public float UvaACoefficient { get; set; } = 2.22f;
        public float UvaBCoefficient { get; set; } = 1.33f;
        public float UvbCCoefficient { get; set; } = 2.95f;
        public float UvbDCoefficient { get; set; } = 1.74f;
        public float UvaResponse { get; set; } = 0.001461f;
        public float UvbResponse { get; set; } = 0.002591f;

        public ushort Id { get; protected set; }

        public bool IsSampling { get; protected set; } = false;

        public event EventHandler<UVConditionChangeResult> Updated = delegate { };

        protected UVConditions Conditions;
        private object _lock = new object();
        private CancellationTokenSource SamplingTokenSource;

        public VEML6075(II2cBus i2CBus, IntegrationtTme integrationtTme = IntegrationtTme._50MS, bool highDynamic = true)
        {
            _i2CPeripheral = new I2cPeripheral(i2CBus, busAddress);

            Initialize(integrationtTme, highDynamic);
        }

        protected void Initialize(IntegrationtTme integrationtTme = IntegrationtTme._50MS, bool highDynamic = true)
        {
            Id = _i2CPeripheral.ReadUShort(IdRegister, ByteOrder.BigEndian);

            //shutdown
            SetPowerState(false);

            SetIntegrationTime(integrationtTme);

            SetDynamicSetting(highDynamic);

            SetPowerState(true);
        }

        /// <summary>
        /// Start sampling UV measurements.
        /// </summary>
        /// <param name="standbyDuration">Polling duration.</param>
        public void StartUpdating(int standbyDuration = 1000)
        {
            if (standbyDuration <= 0)
                throw new ArgumentException("standbyDuration must be >= 0", nameof(standbyDuration));

            // thread safety
            lock (_lock)
            {
                if (IsSampling) return;

                // state muh-cheen
                IsSampling = true;

                SamplingTokenSource = new CancellationTokenSource();
                CancellationToken ct = SamplingTokenSource.Token;

                UVConditions oldConditions;
                UVConditionChangeResult result;

                Task.Factory.StartNew(async () => {

                    Conditions = await ReadAsync();
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
                        oldConditions = UVConditions.From(Conditions);

                        // read
                        Conditions = await ReadAsync();

                        // build a new result with the old and new conditions
                        result = new UVConditionChangeResult(Conditions, oldConditions);

                        // let everyone know
                        RaiseChangedAndNotify(result);

                        // sleep for the appropriate interval
                        await Task.Delay(standbyDuration);
                    }
                }, SamplingTokenSource.Token);
            }
        }

        /// <summary>
        /// Stops sampling UV measurements.
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

        protected async Task<UVConditions> ReadAsync() 
        {
            return await Task.Run(() => {
                return Reading();
            });
        }

        protected void RaiseChangedAndNotify(UVConditionChangeResult changeResult)
        {
            Updated?.Invoke(this, changeResult);
            base.NotifyObservers(changeResult);
        }

        public UVConditions Reading()
        {
            if (IsForceModeOn())
            {
                var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);
                config |= 0x04;
                _i2CPeripheral.WriteUShort(ConfigRegister, config);
                Thread.Sleep(2);
            }
            else
            {
                Thread.Sleep(1);
            }

            var uva = _i2CPeripheral.ReadUShort(UvaRegister, ByteOrder.BigEndian);
            var uvb = _i2CPeripheral.ReadUShort(UvbRegister, ByteOrder.BigEndian);

            var uvcomp1 = _i2CPeripheral.ReadUShort(UvComp1Register, ByteOrder.BigEndian);
            var uvcomp2 = _i2CPeripheral.ReadUShort(UvComp2Register, ByteOrder.BigEndian);

            var _uvaCalc = uva - (UvaACoefficient * uvcomp1) - (UvaBCoefficient * uvcomp2);
            var _uvbCalc = uvb - (UvbCCoefficient * uvcomp1) - (UvbDCoefficient * uvcomp2);
            var uvIndex = ((_uvaCalc * UvaResponse) + (_uvbCalc * UvbResponse)) / 2;

            return new UVConditions { UVA = _uvaCalc, UVB = _uvbCalc, UVIndex = uvIndex };
        }

        public void SetIntegrationTime(IntegrationtTme itime)
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);

            //Intergation time values are bits 4-6
            var newConfig = (byte)((((byte)itime << 4) & 0b10001111) & config);
            _i2CPeripheral.WriteUShort(ConfigRegister, newConfig);
        }

        public IntegrationtTme GetIntegrationTime()
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);
            return (IntegrationtTme)((byte)((config >> 4) & 0x07));
        }

        public void SetDynamicSetting(bool isHigh)
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);

            if (isHigh)
                config |= 0x08;
            else
                config &= 0b11110111;

            _i2CPeripheral.WriteUShort(ConfigRegister, config);
        }

        public void SetPowerState(bool powerOn)
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);

            if (powerOn)
                config &= 0b11111110;
            else
                config |= 0x01;

            _i2CPeripheral.WriteUShort(ConfigRegister, config);

        }

        public bool IsPowerStateOn()
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);
            return ((config & 0x01) == 0x01) ? false : true;
        }

        public void SetForceMode(bool forceMode)
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);

            if (forceMode)
                config |= 0x02;
            else
                config &= 0b11111101;

            _i2CPeripheral.WriteUShort(ConfigRegister, config);

        }

        public bool IsForceModeOn()
        {
            var config = _i2CPeripheral.ReadUShort(ConfigRegister, ByteOrder.BigEndian);
            return ((config & 0x02) == 0x02) ? true : false;
        }


    }

    public class UVConditions
    {
        public float UVA { get; set; }
        public float UVB { get; set; }
        public float UVIndex { get; set; }

        public UVConditions() { }
        public UVConditions(float uvA, float uvB, float uvIndex)
        {
            UVA = uvA;
            UVB = uvB;
            UVIndex = uvIndex;
        }

        public static UVConditions From(UVConditions conditions)
        {
            return new UVConditions(conditions.UVA, conditions.UVB, conditions.UVIndex);
        }
    }
}

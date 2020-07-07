using System;
using Meadow.Hardware;
using System.Linq;
using System.Threading;
using Meadow.Peripherals.Sensors.Atmospheric;
using System.Threading.Tasks;
using Meadow.Peripherals.Sensors.Temperature;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    public class Bme680 : FilterableObservableBase<AtmosphericConditionChangeResult, AtmosphericConditions>, IAtmosphericSensor
        , ITemperatureSensor, IHumiditySensor, IBarometricPressureSensor
    {
        /// <summary>
        ///     Valid oversampling values.
        /// </summary>
        /// <remarks>
        ///     000 - Data output set to 0x8000
        ///     001 - Oversampling x1
        ///     010 - Oversampling x2
        ///     011 - Oversampling x4
        ///     100 - Oversampling x8
        ///     101, 110, 111 - Oversampling x16
        /// </remarks>
        public enum Oversample : byte
        {
            Skip = 0,
            OversampleX1,
            OversampleX2,
            OversampleX4,
            OversampleX8,
            OversampleX16
        }

        /// <summary>
        ///     Valid values for the operating mode of the sensor.
        /// </summary>
        public enum Modes : byte
        {
            /// <summary>
            /// no operation, all registers accessible, lowest power, selected after startup
            /// </summary>
            Sleep = 0,
            /// <summary>
            /// perform one measurement, store results and return to sleep mode
            /// </summary>
            Forced = 1,
        }

        /// <summary>
        ///     Valid filter co-efficient values.
        /// </summary>
        public enum FilterCoefficient : byte
        {
            Off = 0,
            Two,
            Four,
            Eight,
            Sixteen
        }

        public enum GasModes : byte
        {
            Disabled = 0x00,
            Enabled = 0x10
        }

        public enum I2cAddress : byte
        {
            Adddress0x76 = 0x76,
            Adddress0x77 = 0x77
        }

        /// <summary>
        /// Pressure in hectoPascals at sea level.
        /// Used to calibrate altitude
        /// </summary>
        public float SeaLevelPressure { get; set; } = 1013.25f;

        /// <summary>
        /// The temperature, in degrees celsius (°C), from the last reading.
        /// </summary>
        public float Temperature => Conditions.Temperature.Value;

        /// <summary>
        /// The pressure, in hectopascals (hPa), from the last reading. 1 hPa
        /// is equal to one millibar, or 1/10th of a kilopascal (kPa)/centibar.
        /// </summary>
        public float Pressure => Conditions.Pressure.Value;

        /// <summary>
        /// The humidity, in percent relative humidity, from the last reading..
        /// </summary>
        public float Humidity => Conditions.Humidity.Value;

        /// <summary>
        /// The Gas Resistance, in ohms, from the last reading..
        /// </summary>
        public float GasResistance { get; protected set; }

        /// <summary>
        /// The Aprox. altitude.
        /// Calculated the last pressure reading and the SeaLevelPressure
        /// </summary>
        public float Altitude { get { return CalculateAltitude(Conditions.Pressure.Value, SeaLevelPressure); } }

        /// <summary>
        /// The BME680 Id
        /// </summary>
        public byte ChipId { get; protected set; }

        public AtmosphericConditions Conditions { get; protected set; } = new AtmosphericConditions();

        /// <summary>
        /// Gets a value indicating whether the analog input port is currently
        /// sampling the ADC. Call StartSampling() to spin up the sampling process.
        /// </summary>
        /// <value><c>true</c> if sampling; otherwise, <c>false</c>.</value>
        public bool IsSampling { get; protected set; } = false;

        /// <summary>
        ///     Compensation data.
        /// </summary>
        protected struct CompensationData
        {
            public ushort T1;
            public short T2;
            public sbyte T3;
            public ushort P1;
            public short P2;
            public sbyte P3;
            public short P4;
            public short P5;
            public sbyte P6;
            public sbyte P7;
            public short P8;
            public short P9;
            public sbyte P10;
            public ushort H1;
            public ushort H2;
            public sbyte H3;
            public sbyte H4;
            public sbyte H5;
            public byte H6;
            public sbyte H7;
            public sbyte G1;
            public short G2;
            public sbyte G3;

            public byte HeatRange;
            public sbyte HeatValue;
            public sbyte SwErr;
            public float Fine;
        }

        protected CompensationData _compensationData;
        protected Configuration _configuration;

        private readonly Bme680Comms _bme680;

        // internal thread lock
        private object _lock = new object();
        private CancellationTokenSource SamplingTokenSource;

        public event EventHandler<AtmosphericConditionChangeResult> Updated = delegate { };

        static float[] lookupK1Range = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 0.0f, -0.8f, 0.0f, 0.0f, -0.2f, -0.5f, 0.0f, -1.0f, 0.0f, 0.0f };

        static float[] lookupk2Range = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.1f, 0.7f, 0.0f, -0.8f, -0.1f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };

        public Bme680(II2cBus i2CBus, I2cAddress busAddress = I2cAddress.Adddress0x76)
        {
            _bme680 = new Bme680I2C(i2CBus, (byte)busAddress);
        }

        public Bme680(ISpiBus spi, IDigitalOutputPort chipSelect)
        {
            throw new NotImplementedException();
        }

        public void Initialize()
        {
            _bme680.WriteRegister(_bme680.ResetRegister, 0xB6);
            Thread.Sleep(1);

            ChipId = _bme680.ReadRegister(_bme680.ChipIdRegister);

            ReadCompensationData();

            _configuration = new Configuration();
            _configuration.Mode = Modes.Sleep;
            _configuration.Filter = FilterCoefficient.Four;
            _configuration.TemperatureOverSampling = Oversample.OversampleX8;
            _configuration.PressureOversampling = Oversample.OversampleX4;
            _configuration.HumidityOverSampling = Oversample.OversampleX2;
            _configuration.GasMode = GasModes.Enabled;

            //set up heater
            _bme680.WriteRegister(Bme680Comms.Register.Heat0Register, 0x73);
            _bme680.WriteRegister(Bme680Comms.Register.GasWaitRegister, 0x65);
        }

        public async Task<Bme680AtmosphericConditions> ReadAsync(
            Oversample temperatureSampleCount = Oversample.OversampleX8,
            Oversample pressureSampleCount = Oversample.OversampleX4,
            Oversample humiditySampleCount = Oversample.OversampleX2,
            GasModes gasMode = GasModes.Enabled,
            FilterCoefficient filter = FilterCoefficient.Four)
        {
            // update confiruation for a one-off read
            _configuration.TemperatureOverSampling = temperatureSampleCount;
            _configuration.PressureOversampling = pressureSampleCount;
            _configuration.HumidityOverSampling = humiditySampleCount;
            _configuration.GasMode = gasMode;
            _configuration.Mode = Modes.Forced;
            _configuration.Filter = filter;


            UpdateConfiguration(_configuration);

            var bme680Conditions = await ReadAsync();
            Conditions = bme680Conditions.Atmospheric;
            GasResistance = bme680Conditions.GasResistance;

            return bme680Conditions;
        }

        public void StartUpdating(
            Oversample temperatureSampleCount = Oversample.OversampleX8,
            Oversample pressureSampleCount = Oversample.OversampleX4,
            Oversample humiditySampleCount = Oversample.OversampleX2,
            GasModes gasMode = GasModes.Enabled,
            FilterCoefficient filter = FilterCoefficient.Four,
            int standbyDuration = 1000)
        {

            // thread safety
            lock (_lock)
            {
                if (IsSampling) return;

                // state muh-cheen
                IsSampling = true;

                SamplingTokenSource = new CancellationTokenSource();
                CancellationToken ct = SamplingTokenSource.Token;

                AtmosphericConditions oldConditions;
                AtmosphericConditionChangeResult result;
                Task.Factory.StartNew(async () => {
                    while (true)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            // do task clean up here
                            _observers.ForEach(x => x.OnCompleted());
                            break;
                        }
                        // capture history
                        oldConditions = AtmosphericConditions.From(Conditions);

                        // read
                        await ReadAsync(temperatureSampleCount, pressureSampleCount, humiditySampleCount, gasMode, filter);

                        // build a new result with the old and new conditions
                        result = new AtmosphericConditionChangeResult(oldConditions, Conditions);

                        // let everyone know
                        RaiseChangedAndNotify(result);

                        // sleep for the appropriate interval
                        await Task.Delay(standbyDuration);
                    }
                }, SamplingTokenSource.Token);
            }
        }

        protected void RaiseChangedAndNotify(AtmosphericConditionChangeResult changeResult)
        {
            Updated?.Invoke(this, changeResult);
            base.NotifyObservers(changeResult);
        }

        /// <summary>
        /// Stops sampling the temperature.
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

        protected async Task<Bme680AtmosphericConditions> ReadAsync()
        {
            return await Task.Run(() =>
            {
                Bme680AtmosphericConditions conditions = new Bme680AtmosphericConditions();

                var ctrl = _bme680.ReadRegister(Bme680Comms.Register.MeasurementRegister);
                ctrl = (byte)((ctrl & 0xFC) | 0x01);//  # enable single shot!
                _bme680.WriteRegister(Bme680Comms.Register.MeasurementRegister, ctrl);

                byte tries = 10;
                while (tries > 0)
                {
                    var buff = _bme680.ReadRegisters(0x1D, 15);
                    var status = buff[0] & 0x80;
                    var gas_index = buff[0] & 0x0f;
                    var meas_index = buff[1];

                    var presADC = (uint)(((uint)buff[2] * 4096) | ((uint)buff[3] * 16)
                        | ((uint)buff[4] / 16));

                    var tempADC = (uint)(((uint)buff[5] * 4096) | ((uint)buff[6] * 16)
                        | ((uint)buff[7] / 16));

                    var humADC = (ushort)(((uint)buff[8] * 256) | (uint)buff[9]);
                    var gasResADC = (ushort)((uint)buff[13] * 4 | (((uint)buff[14]) / 64));
                    var gasRange = (byte)(buff[14] & 0x0f);

                    status |= buff[14] & 0x20;
                    status |= buff[14] & 0x10;

                    if (((byte)status & 0x80) == 0x80)
                    {
                        tries = 0;
                        conditions.Atmospheric.Temperature = CalculateTemperature(tempADC, _compensationData);
                        conditions.Atmospheric.Pressure = CalculatePressure(presADC, _compensationData);
                        conditions.Atmospheric.Humidity = CalculateHumidity(humADC, _compensationData);
                        conditions.GasResistance = CalculateGasResistance(gasResADC, gasRange, _compensationData);

                    }
                    else
                    {
                        Task.Delay(20);
                        tries--;
                    }

                }

                return conditions;
            });
        }

        void UpdateConfiguration(Configuration configuration)
        {
            //
            //  Put to sleep to allow the configuration to be changed.
            //
            _bme680.WriteRegister(Bme680Comms.Register.MeasurementRegister, 0x00);

            _bme680.WriteRegister(Bme680Comms.Register.ConfigRegister, (byte)(((byte)configuration.Filter) << 2));

            //Temperature + Pressure
            var measurementRegisterData = (byte)((byte)(configuration.TemperatureOverSampling) << 5 | (byte)(configuration.PressureOversampling) << 2);
            _bme680.WriteRegister(Bme680Comms.Register.MeasurementRegister, measurementRegisterData);

            //Humidity
            _bme680.WriteRegister(Bme680Comms.Register.HumidityControlRegister, (byte)configuration.HumidityOverSampling);

            //Gas
            _bme680.WriteRegister(Bme680Comms.Register.GasControlRegister, (byte)configuration.GasMode);
        }

        void ReadCompensationData()
        {
            var coeff1 = _bme680.ReadRegisters(0x89, 25);
            var coeff2 = _bme680.ReadRegisters(0xE1, 16);
            var coeffData = coeff1.ToList();
            coeffData.AddRange(coeff2.ToList());

            _compensationData.T1 = (ushort)(coeffData[33] | (coeffData[34] << 8));
            _compensationData.T2 = (short)(coeffData[1] | (coeffData[2] << 8));
            _compensationData.T3 = (sbyte)(coeffData[3]);

            _compensationData.P1 = (ushort)(coeffData[5] + (coeffData[6] << 8));
            _compensationData.P2 = (short)(coeffData[7] + (coeffData[8] << 8));
            _compensationData.P3 = (sbyte)(coeffData[9]);
            _compensationData.P4 = (short)(coeffData[11] + (coeffData[12] << 8));
            _compensationData.P5 = (short)(coeffData[13] + (coeffData[14] << 8));
            _compensationData.P6 = (sbyte)(coeffData[16]);
            _compensationData.P7 = (sbyte)(coeffData[15]);
            _compensationData.P8 = (short)(coeffData[19] + (coeffData[20] << 8));
            _compensationData.P9 = (short)(coeffData[21] + (coeffData[22] << 8));
            _compensationData.P10 = (sbyte)(coeffData[23]);

            _compensationData.H1 = (ushort)((coeffData[27] << 4) + (coeffData[26] & 0xf));
            _compensationData.H2 = (ushort)(((coeffData[25]) << 4) + (coeffData[26] >> 4));
            _compensationData.H3 = (sbyte)(coeffData[28]);
            _compensationData.H4 = (sbyte)(coeffData[29]);
            _compensationData.H5 = (sbyte)(coeffData[30]);
            _compensationData.H6 = (byte)(coeffData[31]);
            _compensationData.H7 = (sbyte)(coeffData[32]);

            _compensationData.G1 = (sbyte)(coeffData[37]);
            _compensationData.G2 = (short)(coeffData[35] + (coeffData[36] << 8));
            _compensationData.G3 = (sbyte)(coeffData[38]);

            _compensationData.HeatRange = (byte)((_bme680.ReadRegister(0x02) & 0x30) / 16);
            _compensationData.HeatValue = (sbyte)(_bme680.ReadRegister((byte)0x00));
            _compensationData.SwErr = (sbyte)((_bme680.ReadRegister(0x04) & 0xF0) / 16);
        }

        static float CalculateTemperature(uint tempADC, CompensationData compensationData)
        {
            float var1 = 0;
            float var2 = 0;
            float calc_temp = 0;

            /* calculate var1 data */
            var1 = ((tempADC / 16384.0f) - (compensationData.T1 / 1024.0f))
                * (compensationData.T2);

            /* calculate var2 data */
            var2 = (((tempADC / 131072.0f) - (compensationData.T1 / 8192.0f)) *
                ((tempADC / 131072.0f) - (compensationData.T1 / 8192.0f))) *
                (compensationData.T3 * 16.0f);

            /* fine value*/
            compensationData.Fine = (var1 + var2);

            /* compensated temperature data*/
            calc_temp = ((compensationData.Fine) / 5120.0f);

            return calc_temp;
        }

        static float CalculatePressure(uint presADC, CompensationData compensationData)
        {
            float var1 = 0;
            float var2 = 0;
            float var3 = 0;
            float calc_pres = 0;

            var1 = (((float)compensationData.Fine / 2.0f) - 64000.0f);
            var2 = var1 * var1 * (((float)compensationData.P6) / (131072.0f));
            var2 = var2 + (var1 * ((float)compensationData.P5) * 2.0f);
            var2 = (var2 / 4.0f) + (((float)compensationData.P4) * 65536.0f);
            var1 = (((((float)compensationData.P3 * var1 * var1) / 16384.0f)
                + ((float)compensationData.P2 * var1)) / 524288.0f);
            var1 = ((1.0f + (var1 / 32768.0f)) * ((float)compensationData.P1));
            calc_pres = (1048576.0f - ((float)presADC));

            /* Avoid exception caused by division by zero */
            if ((int)var1 != 0)
            {
                calc_pres = (((calc_pres - (var2 / 4096.0f)) * 6250.0f) / var1);
                var1 = (((float)compensationData.P9) * calc_pres * calc_pres) / 2147483648.0f;
                var2 = calc_pres * (((float)compensationData.P8) / 32768.0f);
                var3 = ((calc_pres / 256.0f) * (calc_pres / 256.0f) * (calc_pres / 256.0f)
                    * (compensationData.P10 / 131072.0f));
                calc_pres = (calc_pres + (var1 + var2 + var3 + ((float)compensationData.P7 * 128.0f)) / 16.0f);
                calc_pres /= 100;
            }
            else
            {
                calc_pres = 0;
            }

            //Console.WriteLine($"calc_pres: {calc_pres}");
            return calc_pres;
        }

        static float CalculateHumidity(ushort humADC, CompensationData compensationData)
        {
            float temp_comp;

            /* compensated temperature data*/
            temp_comp = ((compensationData.Fine) / 5120.0f);
            float var1 = humADC - (compensationData.H1 * 16.0f + compensationData.H3 / 2.0f * temp_comp);

            float var2 = var1 * (float)(compensationData.H2 / 262144.0f * (1.0f + compensationData.H4 / 16384.0f * temp_comp + compensationData.H5 / 1048576.0f * temp_comp * temp_comp));

            float var3 = compensationData.H6 / 16384.0f;

            float var4 = compensationData.H7 / 2097152.0f;

            float calc_hum = var2 + (var3 + var4 * temp_comp) * var2 * var2;

            if (calc_hum > 100.0f)
                calc_hum = 100.0f;
            else if (calc_hum < 0.0f)
                calc_hum = 0.0f;


            return calc_hum;
        }

        static float CalculateGasResistance(ushort GasResADC, byte GasRange, CompensationData compensationData)
        {
            float calc_gas_res;

            float var1 = 1340.0f + 5.0f * compensationData.SwErr;
            float var2 = var1 * (1.0f + lookupK1Range[GasRange] / 100.0f);
            float var3 = 1.0f + lookupk2Range[GasRange] / 100.0f;

            calc_gas_res = 1.0f / (float)(var3 * (0.000000125f) * (float)(1 << GasRange) * (((((float)GasResADC)
                - 512.0f) / var2) + 1.0f));

            return calc_gas_res;
        }

        static float CalculateAltitude(float pressure, float seaLevelPressure)
        {
            return (float)(44330.0 * (1.0 - Math.Pow((pressure / seaLevelPressure), 0.1903)));
        }
        public class Configuration
        {
            /// <summary>
            ///     Temperature over sampling configuration.
            /// </summary>
            public Oversample TemperatureOverSampling { get; set; }

            /// <summary>
            ///     Pressure over sampling configuration.
            /// </summary>
            public Oversample PressureOversampling { get; set; }

            /// <summary>
            ///     Humidity over sampling configuration.
            /// </summary>
            public Oversample HumidityOverSampling { get; set; }

            /// <summary>
            ///     Set the operating mode for the sensor.
            /// </summary>
            public Modes Mode { get; set; }


            /// <summary>
            ///     Determine the time constant for the IIR filter.
            /// </summary>
            public FilterCoefficient Filter { get; set; }

            public GasModes GasMode { get; set; }
        }

        public class Bme680AtmosphericConditions
        {
            public AtmosphericConditions Atmospheric { get; set; } = new AtmosphericConditions();
            public float GasResistance { get; set; }

        }
    }
}

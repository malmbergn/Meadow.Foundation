using System;
using Meadow.Hardware;
using System.Linq;
using System.Threading;
using Meadow.Peripherals.Sensors.Atmospheric;
using System.Threading.Tasks;
using Meadow.Peripherals.Sensors.Temperature;

namespace Meadow.Foundation.Sensors.Atmospheric
{
    public class Bme680 : FilterableObservableBase<AtmosphericConditionPlusGasChangeResult, AtmosphericPlusGasConditions>
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

        public enum GasHeaterProfiles : byte
        {
            Zero = 0,
            One,
            Two,
            Three,
            Four,
            Five,
            Six,
            Seven,
            Eight,
            Nine

        }
        public enum GasModes : byte
        {
            Disabled = 0x00,
            Enabled = 0x01
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
        public float GasResistance => Conditions.GasResistance.Value;

        /// <summary>
        /// The Aprox. altitude.
        /// Calculated the last pressure reading and the SeaLevelPressure
        /// </summary>
        public float Altitude => Conditions.Altitude.Value;


        /// <summary>
        /// The BME680 Id
        /// </summary>
        public byte ChipId { get; protected set; }

        public AtmosphericPlusGasConditions Conditions { get; protected set; } = new AtmosphericPlusGasConditions();

        /// <summary>
        /// Gets a value indicating whether the analog input port is currently
        /// sampling the ADC. Call StartSampling() to spin up the sampling process.
        /// </summary>
        /// <value><c>true</c> if sampling; otherwise, <c>false</c>.</value>
        public bool IsSampling { get; protected set; } = false;

        public GasHeaterProfiles GasHeaterProfile { get; set; } = GasHeaterProfiles.Zero;

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
        protected float _ambientTemperature;

        private readonly Bme680Comms _bme680;

        // internal thread lock
        private object _lock = new object();
        private CancellationTokenSource SamplingTokenSource;

        public event EventHandler<AtmosphericConditionPlusGasChangeResult> Updated = delegate { };

        protected static float[] lookupK1Range = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 0.0f, -0.8f, 0.0f, 0.0f, -0.2f, -0.5f, 0.0f, -1.0f, 0.0f, 0.0f };

        protected static float[] lookupk2Range = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.1f, 0.7f, 0.0f, -0.8f, -0.1f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };

        public Bme680(II2cBus i2CBus, I2cAddress busAddress = I2cAddress.Adddress0x76)
        {
            _bme680 = new Bme680I2C(i2CBus, (byte)busAddress);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="spi">SPI BUS</param>
        /// <param name="chipSelect">SPI Chip select pin</param>
        /// <remarks>
        /// NOT TESTED
        /// </remarks>
        public Bme680(ISpiBus spi, IDigitalOutputPort chipSelect)
        {
            _bme680 = new Bme680SPI(spi, chipSelect);
        }

        public virtual void Initialize()
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

            SetGasHeaterProfile(320, 150);
            
            _ambientTemperature = 25;
        }

        public virtual async Task<AtmosphericPlusGasConditions> ReadAsync(
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
            _configuration.Filter = filter;

            Conditions = await ReadAsync();

            return Conditions;
        }

        public virtual void StartUpdating(
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

                ReadAsync(temperatureSampleCount, pressureSampleCount, humiditySampleCount, gasMode, filter).Wait();

                SamplingTokenSource = new CancellationTokenSource();
                CancellationToken ct = SamplingTokenSource.Token;

                AtmosphericPlusGasConditions oldConditions;
                AtmosphericConditionPlusGasChangeResult result;

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
                        oldConditions = AtmosphericPlusGasConditions.From(Conditions);

                        // read
                        await ReadAsync(temperatureSampleCount, pressureSampleCount, humiditySampleCount, gasMode, filter);

                        // build a new result with the old and new conditions
                        result = new AtmosphericConditionPlusGasChangeResult(oldConditions, Conditions);

                        // let everyone know
                        RaiseChangedAndNotify(result);

                        // sleep for the appropriate interval
                        await Task.Delay(standbyDuration);
                    }
                }, SamplingTokenSource.Token);
            }
        }

        protected void RaiseChangedAndNotify(AtmosphericConditionPlusGasChangeResult changeResult)
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

        protected async Task<AtmosphericPlusGasConditions> ReadAsync()
        {
            return await Task.Run(() =>
            {

                UpdateConfiguration(_configuration);

                AtmosphericPlusGasConditions conditions = new AtmosphericPlusGasConditions();

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

                    var presADC = (uint)(((uint)buff[2] * 4096) | ((uint)buff[3] * 16) | ((uint)buff[4] / 16));

                    var tempADC = (uint)(((uint)buff[5] * 4096) | ((uint)buff[6] * 16) | ((uint)buff[7] / 16));

                    var humADC = (ushort)(((uint)buff[8] * 256) | (uint)buff[9]);
                    var gasResADC = (ushort)((uint)buff[13] * 4 | (((uint)buff[14]) / 64));
                    var gasRange = (byte)(buff[14] & 0x0f);

                    status |= buff[14] & 0x20;
                    status |= buff[14] & 0x10;

                    if (((byte)status & 0x80) == 0x80)
                    {
                        tries = 0;

                        conditions.Temperature = CalculateTemperature(tempADC);
                        conditions.Pressure = CalculatePressure(presADC);
                        conditions.Humidity = CalculateHumidity(humADC);
                        conditions.GasResistance = CalculateGasResistance(gasResADC, gasRange);
                        conditions.Altitude = CalculateAltitude(conditions.Pressure.Value, SeaLevelPressure);
                        _ambientTemperature = conditions.Temperature.Value;
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

        protected void UpdateConfiguration(Configuration configuration)
        {
            //  Put to sleep to allow the configuration to be changed.
            _bme680.WriteRegister(Bme680Comms.Register.MeasurementRegister, 0x00);

            _bme680.WriteRegister(Bme680Comms.Register.ConfigRegister, (byte)(((byte)configuration.Filter) << 2));

            //Temperature + Pressure
            var measurementRegisterData = (byte)((byte)(configuration.TemperatureOverSampling) << 5 | (byte)(configuration.PressureOversampling) << 2);
            _bme680.WriteRegister(Bme680Comms.Register.MeasurementRegister, measurementRegisterData);

            //Humidity
            _bme680.WriteRegister(Bme680Comms.Register.HumidityControlRegister, (byte)configuration.HumidityOverSampling);

            //Gas
            SetGasStatus(_configuration.GasMode);
        }

        protected void ReadCompensationData()
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

        float CalculateTemperature(uint tempAdc)
        {

            /* calculate var1 data */
            float var1 = (tempAdc / 16384.0f - _compensationData.T1 / 1024.0f)
                * _compensationData.T2;

            /* calculate var2 data */
            float var2 = (tempAdc / 131072.0f - _compensationData.T1 / 8192.0f) *
                (tempAdc / 131072.0f - _compensationData.T1 / 8192.0f) *
                (_compensationData.T3 * 16.0f);

            /* t_fine value*/
            _compensationData.Fine = (var1 + var2);

            /* compensated temperature data*/
            float temp = _compensationData.Fine / 5120.0f;

            return temp;
        }

        float CalculatePressure(uint presAdc)
        {
            float var1 = _compensationData.Fine / 2.0f - 64000.0f;
            float var2 = var1 * var1 * (_compensationData.P6 / 131072.0f);

            var2 = var2 + (var1 * ((float)_compensationData.P5) * 2.0f);
            var2 = (var2 / 4.0f) + (((float)_compensationData.P4) * 65536.0f);
            var1 = (((((float)_compensationData.P3 * var1 * var1) / 16384.0f) + ((float)_compensationData.P2 * var1)) / 524288.0f);
            var1 = ((1.0f + (var1 / 32768.0f)) * ((float)_compensationData.P1));

            float pressure = 1048576.0f - presAdc;

            /* Avoid exception caused by division by zero */
            if ((int)var1 != 0)
            {
                pressure = (((pressure - (var2 / 4096.0f)) * 6250.0f) / var1);
                var1 = (((float)_compensationData.P9) * pressure * pressure) / 2147483648.0f;
                var2 = pressure * (((float)_compensationData.P8) / 32768.0f);

                float var3 = ((pressure / 256.0f) * (pressure / 256.0f) * (pressure / 256.0f) * (_compensationData.P10 / 131072.0f));
                pressure = (pressure + (var1 + var2 + var3 + ((float)_compensationData.P7 * 128.0f)) / 16.0f);
                pressure /= 100;
            }
            else
            {
                pressure = 0;
            }

            //Console.WriteLine($"calc_pres: {calc_pres}");
            return pressure;
        }

        float CalculateHumidity(ushort humAdc)
        {

            /* compensated temperature data*/
            float tempComp = ((_compensationData.Fine) / 5120.0f);
            float var1 = humAdc - (_compensationData.H1 * 16.0f + _compensationData.H3 / 2.0f * tempComp);

            float var2 = var1 * (float)(_compensationData.H2 / 262144.0f * (1.0f + _compensationData.H4 / 16384.0f * tempComp + _compensationData.H5 / 1048576.0f * tempComp * tempComp));

            float var3 = _compensationData.H6 / 16384.0f;

            float var4 = _compensationData.H7 / 2097152.0f;

            float humidity = var2 + (var3 + var4 * tempComp) * var2 * var2;

            if (humidity > 100.0f)
                humidity = 100.0f;
            else if (humidity < 0.0f)
                humidity = 0.0f;

            //Console.WriteLine($"calc_hum: {calc_hum}");

            return humidity;
        }

        float CalculateGasResistance(ushort gasResAdc, byte gasRange)
        {
            float calcGasRes;

            float[] lookupK1Range = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 0.0f, -0.8f, 0.0f, 0.0f, -0.2f, -0.5f, 0.0f, -1.0f, 0.0f, 0.0f };

            float[] lookupK2Range = new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.1f, 0.7f, 0.0f, -0.8f, -0.1f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f };

            float var1 = 1340.0f + 5.0f * _compensationData.SwErr;
            float var2 = var1 * (1.0f + lookupK1Range[gasRange] / 100.0f);
            float var3 = 1.0f + lookupK2Range[gasRange] / 100.0f;

            calcGasRes = 1.0f / (float)(var3 * (0.000000125f) * (float)(1 << gasRange) * (((((float)gasResAdc) - 512.0f) / var2) + 1.0f));

            return calcGasRes;
        }

        protected float CalculateAltitude(float pressure, float seaLevelPressure)
        {
            return (float)(44330.0 * (1.0 - Math.Pow((pressure / seaLevelPressure), 0.1903)));
        }

        public void SetGasStatus(GasModes mode)
        {
            var data = (byte)(((byte)mode << 4) | (byte)GasHeaterProfile);
            _bme680.WriteRegister(Bme680Comms.Register.GasControlRegister, data);
        }

        public void SelectGasHeaterProfile(GasHeaterProfiles profile)
        {
            GasHeaterProfile = profile;
            _bme680.WriteRegister(Bme680Comms.Register.GasControlRegister, (byte)((byte)_configuration.GasMode << 4 + (byte)profile));
        }

        public void SetGasHeaterProfile(ushort temperature, ushort duration)
        {
            SetGasHeaterTemperature(temperature, (byte)GasHeaterProfile);
            SetGasHeaterDuration(duration, (byte)GasHeaterProfile);
        }

        void SetGasHeaterTemperature(ushort temperature, byte profileNum)
        {
            var temp = CalculateHeaterResistance(temperature);
            _bme680.WriteRegister(Bme680Comms.Register.Heat0Register + profileNum, temp);
        }

        void SetGasHeaterDuration(ushort duration, byte profileNum)
        {
            var dur = CalculateHeaterDuration(duration);
            _bme680.WriteRegister(Bme680Comms.Register.GasWait0Register + profileNum, dur);
        }

        /// <summary>
        /// Convert raw heater resistance using calibration data.
        /// </summary>
        /// <param name="temp">Target temperature</param>
        /// <returns></returns>
        byte CalculateHeaterResistance(ushort temp)
        {
            float var1 = 0;
            float var2 = 0;
            float var3 = 0;
            float var4 = 0;
            float var5 = 0;
            byte resHeat = 0;

            if (temp < 200)
                temp = 200;

            if (temp > 400)
                temp = 400;

            var1 = (((float)_compensationData.G1 / (16.0f)) + 49.0f);
            var2 = ((((float)_compensationData.G2 / (32768.0f)) * (0.0005f)) + 0.00235f);
            var3 = ((float)_compensationData.G3 / (1024.0f));
            var4 = (var1 * (1.0f + (var2 * (float)temp)));
            var5 = (var4 + (var3 * (float)_ambientTemperature));
            resHeat = (byte)(3.4f * ((var5 * (4 / (4 + (float)_compensationData.HeatRange)) * (1 / (1 + ((float)_compensationData.HeatValue * 0.002f)))) - 25));


            return resHeat;
        }

        /// <summary>
        /// Calculate correct value for heater duration setting from milliseconds.
        /// </summary>
        /// <param name="duration">Target duration in milliseconds, between 1 and 4032</param>
        /// <returns></returns>
        byte CalculateHeaterDuration(ushort duration)
        {
            byte factor = 0;
            byte durationVal;

            if (duration >= 0xfc0)
                durationVal = 0xff; // Max duration
            else
            {
                while (duration > 0x3F)
                {
                    duration = (ushort)(duration / 4);
                    factor += 1;
                }

                durationVal = (byte)(duration + (factor * 64));
            }

            return durationVal;
        }

        protected class Configuration
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

    }



}

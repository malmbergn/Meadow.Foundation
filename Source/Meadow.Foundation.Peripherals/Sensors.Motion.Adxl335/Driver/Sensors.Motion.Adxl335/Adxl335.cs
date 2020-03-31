using Meadow.Foundation.Spatial;
using Meadow.Hardware;
using Meadow.Peripherals.Sensors.Motion;
using System;
using System.Threading.Tasks;

namespace Meadow.Foundation.Sensors.Motion
{
    /// <summary>
    /// Driver for the ADXL335 triple axis accelerometer.
    /// +/- 3g
    /// </summary>
    public class Adxl335
        : FilterableObservableBase<AccelerationPositionChangeResult, AccelerationPosition>, IAccelerometer
    {
        #region Constants

        /// <summary>
        /// Minimum value that can be used for the update interval when the
        /// sensor is being configured to generate interrupts.
        /// </summary>
        public const ushort MinimumPollingPeriod = 100;

        #endregion Constants

        /// <summary>
        /// Raised when the value of the reading changes.
        /// </summary>
        public event EventHandler<AccelerationPositionChangeResult> Updated = delegate { };

        #region Properties

        /// <summary>
        /// Analog input channel connected to the x axis.
        /// </summary>
        public IAnalogInputPort XInputPort { get; protected set; }

        /// <summary>
        /// Analog input channel connected to the x axis.
        /// </summary>
        public IAnalogInputPort YInputPort { get; protected set; }

        /// <summary>
        /// Analog input channel connected to the x axis.
        /// </summary>
        public IAnalogInputPort ZInputPort { get; protected set; }

        /// <summary>
        /// Acceleration along the X-axis.
        /// </summary>
        /// <remarks>
        /// This property will only contain valid data after a call to Read or after
        /// an interrupt has been generated.
        /// </remarks>
        public float XAcceleration { get; private set; }

        /// <summary>
        /// Acceleration along the Y-axis.
        /// </summary>
        /// <remarks>
        /// This property will only contain valid data after a call to Read or after
        /// an interrupt has been generated.
        /// </remarks>
        public float YAcceleration { get; private set; }

        /// <summary>
        /// Acceleration along the Z-axis.
        /// </summary>
        /// <remarks>
        /// This property will only contain valid data after a call to Read or after
        /// an interrupt has been generated.
        /// </remarks>
        public float ZAcceleration { get; private set; }

        /// <summary>
        /// Volts per G for the X axis.
        /// </summary>
        public double XVoltsPerG { get; set; }

        /// <summary>
        /// Volts per G for the X axis.
        /// </summary>
        public double YVoltsPerG { get; set; }

        /// <summary>
        /// Volts per G for the X axis.
        /// </summary>
        public double ZVoltsPerG { get; set; }

        /// <summary>
        /// Power supply voltage applied to the sensor.  This will be set (in the constructor)
        /// to 3.3V by default.
        /// </summary>
        public double SupplyVoltage
        {
            get => _supplyVoltage;
            set
            {
                _supplyVoltage = value;
                _zeroGVoltage = value / 2;
            }
        }
        protected double _supplyVoltage;

        #endregion Properties

        #region Member variables / fields

        /// <summary>
        /// Voltage that represents 0g.  This is the supply voltage / 2.
        /// </summary>
        protected double _zeroGVoltage;

        /// <summary>
        /// How often should this sensor be read?
        /// </summary>
        protected readonly ushort _updateInterval = 100;

        /// <summary>
        /// Last X acceleration reading from the sensor.
        /// </summary>
        protected double _lastX;

        /// <summary>
        /// Last Y reading from the sensor.
        /// </summary>
        protected double _lastY;

        /// <summary>
        /// Last Z reading from the sensor.
        /// </summary>
        protected double _lastZ;

        #endregion Member variables / fields

        #region Constructors

        /// <summary>
        /// Make the default constructor private so that the developer cannot access it.
        /// </summary>
        private Adxl335() {}

        /// <summary>
        /// Create a new ADXL335 sensor object.
        /// </summary>
        /// <param name="x">Analog pin connected to the X axis output from the ADXL335 sensor.</param>
        /// <param name="y">Analog pin connected to the Y axis output from the ADXL335 sensor.</param>
        /// <param name="z">Analog pin connected to the Z axis output from the ADXL335 sensor.</param>
        /// <param name="updateInterval">Update interval for the sensor, set to 0 to put the sensor in polling mode.</param>        
        public Adxl335(IIODevice device, IPin x, IPin y, IPin z, ushort updateInterval = 100)
            :this (device.CreateAnalogInputPort(x), device.CreateAnalogInputPort(y), device.CreateAnalogInputPort(z), updateInterval) { }

        public Adxl335(IAnalogInputPort xInputPort, IAnalogInputPort yInputPort, IAnalogInputPort zInputPort, ushort updateInterval = 100)
        {
            if ((updateInterval != 0) && (updateInterval < MinimumPollingPeriod))
            {
                throw new ArgumentOutOfRangeException(nameof(updateInterval),
                    "Update interval should be 0 or greater than " + MinimumPollingPeriod);
            }

            XInputPort = xInputPort;
            YInputPort = yInputPort;
            ZInputPort = zInputPort;

            //
            //  Now set the default calibration data.
            //
            XVoltsPerG = 0.325;
            YVoltsPerG = 0.325;
            ZVoltsPerG = 0.550;
            SupplyVoltage = 3.3;

            if (updateInterval > 0)
            {
                var t = StartUpdating();
            }
            else
            {
                Update().RunSynchronously();
            }

            InitSubscriptions();
        }

        void InitSubscriptions()
        {
            XInputPort.Subscribe
            (
                new FilterableObserver<FloatChangeResult, float>(
                    x => {
                        XAcceleration = x.New;
                        var newX = x.New;
                        var oldX = x.Old;
                        var y = YAcceleration;
                        var z = ZAcceleration;

                        RaiseEventsAndNotify
                        (
                            new AccelerationPositionChangeResult(
                                new AccelerationPosition(newX, y, z),
                                new AccelerationPosition(oldX, y, z)
                            )
                        );
                    }
                )
            );

            YInputPort.Subscribe
            (
                new FilterableObserver<FloatChangeResult, float>(
                    y => {
                        var x = XAcceleration;
                        YAcceleration = y.New;
                        var newY = y.New;
                        var oldY = y.Old;                        
                        var z = ZAcceleration;

                        RaiseEventsAndNotify
                        (
                            new AccelerationPositionChangeResult(
                                new AccelerationPosition(x, newY, z),
                                new AccelerationPosition(x, oldY, z)
                            )
                        );
                    }
                )
            );

            ZInputPort.Subscribe
            (
                new FilterableObserver<FloatChangeResult, float>(
                    z => {
                        var x = XAcceleration;
                        var y = YAcceleration;
                        ZAcceleration = z.New;
                        var newZ = z.New;
                        var oldZ = z.Old;                        
                        
                        RaiseEventsAndNotify
                        (
                            new AccelerationPositionChangeResult(
                                new AccelerationPosition(x, y, newZ),
                                new AccelerationPosition(y, y, oldZ)
                            )
                        );
                    }
                )
            );
        }

        protected void RaiseEventsAndNotify(AccelerationPositionChangeResult changeResult)
        {
            Updated?.Invoke(this, changeResult);
            base.NotifyObservers(changeResult);
        }

        #endregion Constructors

        #region Methods

        /// <summary>
        /// Start the update process.
        /// </summary>
        private async Task StartUpdating()
        {
            while (true)
            {
                await Update();
                await Task.Delay(_updateInterval);
            }
        }

        /// <summary>
        /// Read the sensor output and convert the sensor readings into acceleration values.
        /// </summary>
        public async Task Update()
        {
            //X = (await _xPort.Read() - _zeroGVoltage) / XVoltsPerG;
            //Y = (await _yPort.Read() - _zeroGVoltage) / YVoltsPerG;
            //Z = (await _zPort.Read() - _zeroGVoltage) / ZVoltsPerG;

            //if (_updateInterval == 0 ||
            //    ((Math.Abs(X - _lastX) > AccelerationChangeNotificationThreshold) ||
            //    (Math.Abs(Y - _lastY) > AccelerationChangeNotificationThreshold) ||
            //    (Math.Abs(Z - _lastZ) > AccelerationChangeNotificationThreshold)))
            //{
            //    var lastNotifiedReading = new Vector(_lastX, _lastY, _lastZ);
            //    var currentReading = new Vector(_lastX = X, _lastY = Y, _lastZ = Z);

            //    AccelerationChanged?.Invoke(this, new SensorVectorEventArgs(lastNotifiedReading, currentReading));
            //}
        }

        /// <summary>
        /// Get the raw analog input values from the sensor.
        /// </summary>
        /// <returns>Vector object containing the raw sensor data from the analog pins.</returns>
        public async Task<Vector> GetRawSensorData()
        {
             return new Vector(await XInputPort.Read(), await YInputPort.Read(), await ZInputPort.Read());
        }

        #endregion Methods
    }
}
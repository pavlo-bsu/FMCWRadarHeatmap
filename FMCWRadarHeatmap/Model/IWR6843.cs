using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.IO;

namespace Pavlo.FMCWRadarHeatmap.Model
{
    /// <summary>
    /// class for Texas instruments (c) Frequency-modulated Continuous-wave radar sensor IWR6843. Model level
    /// </summary>
    public class IWR6843:IDisposable
    {

        /// The sensor uses two serial ports:
        /// CLI port - text configuration commands;
        /// Data port - binary output frames (TLV).
        #region COMports
        private SerialPort? _CLIport;
        /// <summary>
        /// serial port for configuration commands ("Enhanced COM Port" of the CP2105 USB-UART)
        /// </summary>
        public SerialPort? CLIport
        {
            get => _CLIport;
            private set { _CLIport = value; }
        }

        private SerialPort? _DataPort;
        /// <summary>
        /// serial port for output data frames ("Standard COM Port" of the CP2105 USB-UART)
        /// </summary>
        public SerialPort? DataPort
        {
            get => _DataPort;
            private set { _DataPort = value; }
        }

        private string _CLIportName = string.Empty;
        public string CLIportName
        {
            get => _CLIportName;
            protected set { _CLIportName = value; }
        }

        private string _DataPortName = string.Empty;
        public string DataPortName
        {
            get => _DataPortName;
            protected set { _DataPortName = value; }
        }

        private int _CLIbaudRate = 0;
        public int CLIbaudRate
        {
            get => _CLIbaudRate;
            set { _CLIbaudRate = value; }
        }

        private int _DataBaudRate = 0;
        public int DataBaudRate
        {
            get => _DataBaudRate;
            set { _DataBaudRate = value; }
        }

        private Parity _TheParity = Parity.None;
        /// <summary>
        /// parity for both ports
        /// </summary>
        public Parity TheParity
        {
            get => _TheParity;
            set { _TheParity = value; }
        }

        private int _DataBits = -1;
        /// <summary>
        /// data bits for both ports
        /// </summary>
        public int DataBits
        {
            get => _DataBits;
            set { _DataBits = value; }
        }

        private StopBits _StopBits = StopBits.None;
        /// <summary>
        /// stop bits for both ports
        /// </summary>
        public StopBits StopBits
        {
            get => _StopBits;
            set { _StopBits = value; }
        }

        private int _DataReadBufferSize = 0;
        /// <summary>
        /// size of the driver input buffer of the Data port.
        /// [bytes]
        /// </summary>
        public int DataReadBufferSize
        {
            get => _DataReadBufferSize;
            set { _DataReadBufferSize = value; }
        }

        private int _CLItimeOut = -1;
        /// <summary>
        /// read and write timeout of the CLI port.
        /// [ms]
        /// </summary>
        public int CLItimeOut
        {
            get => _CLItimeOut;
            set { _CLItimeOut = value; }
        }

        private int _DataTimeOut = -1;
        /// <summary>
        /// read and write timeout of the Data port.
        /// [ms]
        /// </summary>
        public int DataTimeOut
        {
            get => _DataTimeOut;
            set { _DataTimeOut = value; }
        }

        /// <summary>
        /// close and release both COM ports.
        /// </summary>
        public void ClosePorts()
        {
            SerialPort? cliPort = CLIport;
            SerialPort? dataPort = DataPort;
            CLIport = null;
            DataPort = null;

            // Errors of a port which is already gone (e.g. USB cable is unplugged) are ignored, so both ports are always released
            try
            {
                ReleasePort(cliPort);
            }
            finally
            {
                ReleasePort(dataPort);
            }
        }

        /// <summary>
        /// dispose the port, ignoring errors of a port which is already gone
        /// </summary>
        private static void ReleasePort(SerialPort? port)
        {
            try
            {
                //SerialPort.Close() and SerialPort.Dispose() release the same resources
                port?.Dispose();
            }
            //the driver throws these when the device has been removed
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        #endregion

        /// <summary>
        /// Are both COM ports open.
        /// </summary>
        public bool IsConnectionEstablished
        {
            get => CLIport?.IsOpen == true && DataPort?.IsOpen == true;
        }

        public IWR6843()
        {
            //set values according to TI mmWave SDK 3.x user guide (Out-of-Box demo)
            CLIbaudRate = 115200;
            DataBaudRate = 921600;
            TheParity = Parity.None;
            DataBits = 8;
            StopBits = StopBits.One;

            //range-azimuth heatmap alone is ~8 KB per frame
            DataReadBufferSize = 1024 * 1024;

            //timeouts
            CLItimeOut = 2000;
            DataTimeOut = 1000; // A read returns as soon as any bytes arrive, so the timeout expires only when the sensor sends nothing (e.g. it is stopped).

        }
        /// <param name="cliPortName">name of the CLI port, for example "COM3"</param>
        /// <param name="dataPortName">name of the Data port, for eaxmple "COM2"</param>
        /// <exception cref="ArgumentException">a port name is null or empty, or both names are the same</exception>
        public IWR6843(string cliPortName, string dataPortName) : this()
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cliPortName);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataPortName);
            if (string.Equals(cliPortName.Trim(), dataPortName.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("CLI port and Data port must be different.", nameof(dataPortName));

            this.CLIportName = cliPortName;
            this.DataPortName = dataPortName;
        }

        #region IDisposable by Microsoft recommendation
        private bool disposed = false;

        // realization of IDisposable.
        public void Dispose()
        {
            Dispose(true);
            // suppress finalization (in case a derived class adds a finalizer)
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                //dispose managed resources
                //serialport is a managed resource, which itself owns an unmanaged resource
                //see https://stackoverflow.com/questions/4826702/is-serialport-in-net-unmanaged-resource-is-my-wrapped-class-correct
                ClosePorts();
            }
            //dispose unmanaged resources
            //there is no unmanaged resources, so no finalizer is needed

            disposed = true;
        }
        #endregion

        /// <summary>
        /// open new serial port connections with the sensor (CLI port and Data port)
        /// </summary>
        /// <exception cref="ObjectDisposedException">the object is disposed</exception>
        /// <exception cref="UnauthorizedAccessException">a port is already in use by another application</exception>
        /// <exception cref="IOException">a port is not found or not available</exception>
        /// <exception cref="ArgumentOutOfRangeException">baud rate, parity, data bits, stop bits, a timeout or DataReadBufferSize has an invalid value</exception>
        /// <exception cref="ArgumentException">invalid port name (e.g. empty or not beginning with "COM")</exception>
        public void OpenPorts()
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            //release previous connection, if any
            ClosePorts();

            try
            {
                CLIport = new SerialPort(CLIportName, CLIbaudRate, TheParity, DataBits, StopBits);
                CLIport.NewLine = "\n";
                CLIport.ReadTimeout = CLItimeOut;
                CLIport.WriteTimeout = CLItimeOut;
                CLIport.Open();

                DataPort = new SerialPort(DataPortName, DataBaudRate, TheParity, DataBits, StopBits);
                DataPort.ReadTimeout = DataTimeOut;
                DataPort.WriteTimeout = DataTimeOut;
                //must be set before Open()
                DataPort.ReadBufferSize = DataReadBufferSize;
                DataPort.Open();
            }
            catch
            {
                //close half-open connection, if any
                ClosePorts();
                throw;
            }
        }
    }
}

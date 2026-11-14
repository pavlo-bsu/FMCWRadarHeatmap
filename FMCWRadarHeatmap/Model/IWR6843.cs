using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.IO;
using System.Threading;

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
            // If the sensor is running, it is stopped first.
            //Otherwise, the sensor will keep streaming after the ports are closed
            if (IsSensorRunning)
                TryWriteSensorStop();
            IsSensorRunning = false;

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

        /// <summary>
        /// send "sensorStop" without waiting for the reply, port errors are ignored (port is already gone)
        /// </summary>
        private void TryWriteSensorStop()
        {
            try
            {
                CLIport?.WriteLine("sensorStop");
                //wait until the command is transmitted, the port is closed right after
                CLIport?.BaseStream.Flush();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException) { }
        }
        #endregion

        /// <summary>
        /// Are both COM ports open.
        /// </summary>
        public bool IsConnectionEstablished
        {
            get => CLIport?.IsOpen == true && DataPort?.IsOpen == true;
        }

        private bool _IsSensorRunning = false;
        /// <summary>
        /// Is the sensor started by StartSensorAsync() and not stopped yet
        /// </summary>
        public bool IsSensorRunning
        {
            get => _IsSensorRunning;
            private set { _IsSensorRunning = value; }
        }

        private IWR6843Config? _Config;
        /// <summary>
        /// configuration sent to the sensor by ConfigureAsync(); null if the sensor is not configured
        /// </summary>
        public IWR6843Config? Config
        {
            get => _Config;
            private set { _Config = value; }
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

        #region CLI commands
        /// <summary>
        /// the CLI prompt reply to a command
        /// </summary>
        public const string CLIprompt = "mmwDemo:/>";

        /// <summary>
        /// to allow only one CLI command at a time
        /// </summary>
        private readonly SemaphoreSlim _CLIlock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// request platform and firmware versions of the sensor
        /// </summary>
        /// <returns>reply of the "version" command</returns>
        public async Task<string> RequestVersionAsync()
        {
            string reply = await SendCommandAsync("version").ConfigureAwait(false);
            return GetReplyText(reply, "version");
        }

        /// <summary>
        /// send the .cfg file to the sensor (all commands except "sensorStart")
        /// </summary>
        /// <exception cref="ObjectDisposedException">the object is disposed</exception>
        /// <exception cref="InvalidOperationException">the range-azimuth heatmap is off in the .cfg file, or the CLI port is not open</exception>
        public async Task ConfigureAsync(string cfgFilePath)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Config = null;

            IWR6843Config config = IWR6843Config.Load(cfgFilePath);
            if (!config.IsRangeAzimuthHeatmapEnabled)
                throw new InvalidOperationException("The range-azimuth heatmap is off in the .cfg file: the 5th argument of guiMonitor must be 1.");

            if (IsSensorRunning)
                await StopSensorAsync().ConfigureAwait(false);

            foreach (string command in config.Commands)
            {
                //the sensor is started separately by StartSensorAsync()
                if (command.Split(' ')[0] == "sensorStart")
                    continue;
                await SendCommandAsync(command).ConfigureAwait(false);
            }
            Config = config;
        }

        /// <summary>
        /// start the configured sensor; it starts sending frames to the Data port
        /// </summary>
        /// <exception cref="ObjectDisposedException">the object is disposed</exception>
        /// <exception cref="InvalidOperationException">the sensor is not configured, or the CLI port is not open</exception>
        /// <exception cref="IOException">the sensor rejected the command</exception>
        public async Task StartSensorAsync()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Config == null)
                throw new InvalidOperationException("The sensor is not configured. Call ConfigureAsync() first.");

            //set beforehand: after a timeout the sensor may be running anyway, and ClosePorts() should stop it
            IsSensorRunning = true;
            try
            {
                await SendCommandAsync("sensorStart").ConfigureAwait(false);
            }
            catch (IOException)
            {
                //the sensor rejected the command, i.e. it is not running
                IsSensorRunning = false;
                throw;
            }
        }

        /// <summary>
        /// stop the sensor
        /// </summary>
        /// <exception cref="ObjectDisposedException">the object is disposed</exception>
        public async Task StopSensorAsync()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await SendCommandAsync("sensorStop").ConfigureAwait(false);
            IsSensorRunning = false;
        }

        /// <summary>
        /// send a command to the CLI port and wait for the prompt after the reply
        /// </summary>
        /// <returns>reply including the echo of the command, without the prompt</returns>
        /// <exception cref="InvalidOperationException">the CLI port is not open</exception>
        /// <exception cref="IOException">the sensor rejected the command</exception>
        /// <exception cref="TimeoutException">the sensor does not respond within CLItimeOut</exception>
        private async Task<string> SendCommandAsync(string command)
        {
            SerialPort? portToCheck = CLIport;
            if (portToCheck == null || !portToCheck.IsOpen)
                throw new InvalidOperationException("The CLI port is not open!");
            SerialPort port = portToCheck;

            await _CLIlock.WaitAsync().ConfigureAwait(false);
            try
            {
                string reply;
                try
                {
                    //ReadTo() blocks until the prompt or ReadTimeout, so it runs on a thread pool thread (not via TaskCompletionSource)
                    reply = await Task.Run(() =>
                    {
                        port.DiscardInBuffer();
                        port.WriteLine(command);
                        return port.ReadTo(CLIprompt);
                    }).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException($"The sensor did not respond to \"{command}\" within {port.ReadTimeout} ms.", ex);
                }

                if (reply.Contains("Error") || reply.Contains("not recognized"))
                    throw new IOException($"The sensor rejected \"{command}\": {GetReplyText(reply, command)}");
                return reply;
            }
            finally
            {
                _CLIlock.Release();
            }
        }

        /// <summary>
        /// reply lines without the echo of the command and without empty lines
        /// </summary>
        private static string GetReplyText(string reply, string command)
        {
            var lines = reply
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line != command);
            return string.Join(Environment.NewLine, lines);
        }
        #endregion
    }
}

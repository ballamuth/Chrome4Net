using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Chrome4Net.NativeMessaging;

namespace Echo
{
    /// <summary>
    /// Native Messaging Host.
    /// </summary>
    public class Host
    {
        private static ILog log = LogManager.GetLogger(typeof(Host));

        private ManualResetEvent stop;
        private Port port;

        /// <summary>
        /// Creates a new instance of native messaging host.
        /// </summary>
        public Host()
        {
            port = new Port();
            stop = new ManualResetEvent(false);
        }

        /// <summary>
        /// Starts native message processing.
        /// </summary>
        public void Run()
        {
            log.Info("host started");

            stop.Reset();
            while (!stop.WaitOne(0))
            {
                try
                {
                    string message = port.Read();
                    log.DebugFormat("request message\n{0}", message);
                    JObject request = JObject.Parse(message);

                    JObject reply = new JObject();
                    if (request["source"] != null) reply["source"] = request["destination"];
                    if (request["destination"] != null) reply["destination"] = request["source"];
                    reply["request"] = request;
                    reply["extension"] = "Chrome4Net.Echo";
                    message = reply.ToString(Formatting.None);
                    log.DebugFormat("reply message\n{0}", message);
                    port.Write(message);
                }
                catch (EndOfInputStreamException)
                {
                    log.Debug("end of input stream");
                    stop.Set();
                }
                catch (Exception ex)
                {
                    log.Error("message processing caused an exception", ex);
                    stop.Set();
                    throw ex;
                }
            }

            log.Info("host stopped");
        }

        /// <summary>
        /// Stops native message processing.
        /// </summary>
        public void Stop()
        {
            stop.Set();
        }
    }
}

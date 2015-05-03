using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.IO.Pipes;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Chrome4Net.NativeMessaging;

namespace RelayedEcho
{
    class Echo
    {
        private static ILog log = LogManager.GetLogger(typeof(Echo));

        private PipeStream pipeIn;
        private PipeStream pipeOut;
        private StreamReader pipeReader;
        private StreamWriter pipeWriter;

        private Port port;
        private ManualResetEvent stop;

        public Echo(Options options)
        {
            log.Info("creating echo host");

            log.DebugFormat("create interprocess input pipe (handle={0})", options.pipeIn);
            pipeIn = new AnonymousPipeClientStream(PipeDirection.In, options.pipeIn);
            pipeReader = new StreamReader(pipeIn);

            log.DebugFormat("create interprocess output pipe (handle={0})", options.pipeOut);
            pipeOut = new AnonymousPipeClientStream(PipeDirection.Out, options.pipeOut);
            pipeWriter = new StreamWriter(pipeOut);

            log.Debug("create native messaging port");
            port = new Port(pipeIn, pipeOut);

            log.Debug("create stop event");
            stop = new ManualResetEvent(false);

            log.Debug("synchronize processes");
            string sync = pipeReader.ReadLine();
            log.DebugFormat("sent {0}", sync);
            pipeWriter.WriteLine(sync);
            pipeWriter.Flush();
            log.DebugFormat("received {0}", sync);
            pipeOut.WaitForPipeDrain();

            log.Info("created echo host");
        }

        public void Run()
        {
            log.Info("starting echo host");

            log.Debug("reset stop event");
            stop.Reset();

            log.Debug("start message loop");
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
                    reply["extension"] = "Chrome4Net.Relayed.Echo";
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

            log.Info("echo host stopped");
        }

        public void Stop()
        {
            stop.Set();
        }
    }
}

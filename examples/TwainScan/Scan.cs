using System;
using System.Linq;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using log4net;
using Newtonsoft.Json.Linq;
using TwainDotNet;
using TwainDotNet.WinForms;
using TwainDotNet.TwainNative;
using Chrome4Net.NativeMessaging;

namespace TwainScan
{
    class Scan
    {
        private static ILog log = LogManager.GetLogger(typeof(Scan));

        private PipeStream pipeIn;
        private PipeStream pipeOut;
        private ManualResetEvent sync;

        private Port port;
        private Exception portException;
        private ManualResetEvent stop;

        private Form form;
        private IWindowsMessageHook hook;
        private Twain twain;
        private Exception twainException;
        private ManualResetEvent imageTransferred;
        private ManualResetEvent imageAcquired;
        private JObject imageAcquireRequest;

        private const int chunkSizeLimit = 512 * 1024;
        private Stream image;
        private string imageType;
        private string imageUuid;

        public Scan(Program.Options options)
        {
            log.Debug("creating scan host");

            log.DebugFormat("create interprocess input pipe (handle={0})", options.pipeIn);
            pipeIn = new AnonymousPipeClientStream(PipeDirection.In, options.pipeIn);

            log.DebugFormat("create interprocess output pipe (handle={0})", options.pipeOut);
            pipeOut = new AnonymousPipeClientStream(PipeDirection.Out, options.pipeOut);

            log.Debug("create sync event");
            sync = new ManualResetEvent(false);

            log.Debug("create native messaging port");
            port = new Port(pipeIn, pipeOut);

            log.Debug("create stop event");
            stop = new ManualResetEvent(false);

            log.Debug("create form");
            form = new Form();
            form.TopMost = true;
            form.BringToFront();

            log.Debug("create hook");
            hook = new WinFormsWindowMessageHook(form);

            log.Debug("create image acquired and image transferred events");
            imageAcquired = new ManualResetEvent(false);
            imageTransferred = new ManualResetEvent(false);

            log.Debug("scan host created");
        }

        public Scan Sync(int timeout = Timeout.Infinite)
        {
            log.Debug("synchronize processes");

            log.Debug("clear stop and sync events and clear port exception");
            stop.Reset();
            sync.Reset();
            portException = null;

            log.Debug("begin asynchronous read port");
            port.BeginRead(delegate(IAsyncResult _ar) { ((Scan)_ar.AsyncState).SyncReadCallback(_ar); }, this);

            log.DebugFormat("wait for stop event (timeout={0})", timeout);
            if (!stop.WaitOne(timeout)) throw new TimeoutException("Processes synchronization timeout.");
            log.Debug("stop event");

            log.Debug("check ports exceptions");
            if (portException != null)
            {
                log.Debug("re-throw port exception");
                throw portException;
            }

            log.Debug("check sync event");
            if (!sync.WaitOne(0)) throw new SynchronizationException();

            log.Debug("processes synchronized");
            return this;
        }

        private void SyncReadCallback(IAsyncResult ar)
        {
            try
            {
                log.Debug("end asynchronous read port");
                string message = port.EndRead(ar);

                log.DebugFormat("received message\n{0}", message);

                log.Debug("begin asynchronous write port");
                port.BeginWrite(message, delegate(IAsyncResult _ar) { ((Scan)_ar.AsyncState).SyncWriteCallback(_ar); }, this);
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port", ex);
                portException = ex;
                stop.Set();
            }
        }

        private void SyncWriteCallback(IAsyncResult ar)
        {
            try
            {
                log.Debug("end asynchronous write port");
                port.EndWrite(ar);

                log.Debug("set stop and sync events");
                sync.Set();
                stop.Set();
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port", ex);
                portException = ex;
                stop.Set();
            }
        }

        public Scan Run()
        {
            log.Debug("scan host started");

            log.Debug("clear stop event and clear port exception");
            stop.Reset();
            portException = null;

            log.Debug("begin asynchronous read port");
            port.BeginRead(delegate(IAsyncResult _ar) { ((Scan)_ar.AsyncState).ReadPortCallback(_ar); }, this);

            log.Debug("start windows message processing loop");
            Application.Run();
            log.Debug("windows message processing loop stopped");

            log.Debug("wait for stop event");
            stop.WaitOne();
            log.Debug("stop event");

            log.Debug("check port exception");
            if (portException != null)
            {
                log.Debug("re-throw port exception");
                throw portException;
            }

            log.Info("scan host stopped");
            return this;
        }

        private void ReadPortCallback(IAsyncResult ar)
        {
            log.Debug("end asynchronous read port");
            try
            {
                string message = port.EndRead(ar);
                log.DebugFormat("received message\n{0}", message);

                if (!stop.WaitOne(0))
                {
                    try
                    {
                        JObject request = JObject.Parse(message);
                        ProcessRequest(request);

                        if (!stop.WaitOne(0))
                        {
                            log.Debug("begin asynchronous read port");
                            port.BeginRead(delegate(IAsyncResult _ar) { ((Scan)_ar.AsyncState).ReadPortCallback(_ar); }, this);
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error("unhandled exception port", ex);
                        portException = ex;
                        Application.Exit();
                        stop.Set();
                    }
                }
                else
                {
                    log.Debug("stop signalled");
                }
            }
            catch (EndOfInputStreamException)
            {
                log.Debug("end of native messages stream port");
                Application.Exit();
                stop.Set();
            }
            catch (Exception ex)
            {
                log.Error("unhandled exception port", ex);
                portException = ex;
                Application.Exit();
                stop.Set();
            }
        }

        private Scan ProcessRequest(JObject request)
        {
            log.DebugFormat("process incoming request\n{0}", request.ToString());

            JObject reply = new JObject(
                new JProperty("source", request["destination"]),
                new JProperty("destination", request["source"]),
                new JProperty("action", request["action"])
                );

            if (request["action"] == null) return NoActionSpecified(request, reply);
            else if ((string)request["action"] == "get_version") return GetVersion(request, reply);
            else if ((string)request["action"] == "list_actions") return ListActions(request, reply);
            else if ((string)request["action"] == "acquire_sample") return AcquireSample(request, reply);
            else if ((string)request["action"] == "transfer_image") return TransferImage(request, reply);
            else if ((string)request["action"] == "init_twain") return InitTwain(request, reply);
            else if ((string)request["action"] == "list_sources") return ListSources(request, reply);
            else if ((string)request["action"] == "select_source") return SelectSource(request, reply);
            else if ((string)request["action"] == "acquire_image") return AcquireImage(request, reply);
            else return InvalidActionSpecified(request, reply);
        }

        private Scan GetVersion(JObject request, JObject reply)
        {
            log.Debug("processing 'get_version' action");
            
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            reply.Add("assembly", new JObject(
                new JProperty("name", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name),
                new JProperty("version", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()),
                new JProperty("scheme", "http://semver.org/spec/v2.0.0.html")));
            reply.Add("ok", true);

            return SendReply(reply);
        }

        private Scan ListActions(JObject request, JObject reply)
        {
            log.Debug("processing 'list_actions' action");

            reply.Add("actions", new JArray(
                new JValue("list_actions"),
                new JValue("get_version"),
                new JValue("acquire_sample"),
                new JValue("transfer_image"),
                new JValue("init_twain"),
                new JValue("list_sources"),
                new JValue("select_source"),
                new JValue("acquire_image")
             ));
            reply.Add("ok", true);

            return SendReply(reply);
        }

        private Scan AcquireSample(JObject request, JObject reply)
        {
            log.Debug("processing 'acquire_sample' action");

            image = typeof(Program).Assembly.GetManifestResourceStream("TwainScan.resources.sample.jpg");
            if (image == null) return ReportException(request, reply, "no sample image available");
            imageType = "image/jpeg";
            imageUuid = System.Guid.NewGuid().ToString();

            reply.Add("image", new JObject(
                new JProperty("uuid", imageUuid),
                new JProperty("type", imageType),
                new JProperty("size", image.Length)));
            reply.Add("comment", "sample image acquired, use 'transfer_image' to get image data");
            reply.Add("ok", true);

            return SendReply(reply);
        }

        private Scan TransferImage(JObject request, JObject reply)
        {
            log.Debug("processing 'transfer_image' action");

            if (image == null) return ReportException(request, reply, "no image was acquired", "use 'acquire_image' or 'acquire_sample' first");

            reply.Add("image", new JObject(
                new JProperty("uuid", imageUuid),
                new JProperty("type", imageType),
                new JProperty("size", image.Length)));
            reply.Add("chunk", new JObject(
                new JProperty("start", ""),
                new JProperty("length", ""),
                new JProperty("data", "")));
            reply.Add("ok", true);

            int limit = chunkSizeLimit - (chunkSizeLimit % 3);
            byte[] buf = new byte[Math.Min(image.Length, limit)];
            image.Seek(0, SeekOrigin.Begin);
            for (long start = 0, length = Math.Min(image.Length, limit); start < image.Length; start += length, length = Math.Min(image.Length - start, limit))
            {
                log.DebugFormat("start = {0}, length = {1}, size = {2}", start, length, image.Length);
                long bytes_read = image.Read(buf, 0, (int)length);
                Debug.Assert(bytes_read == length);
                reply["chunk"]["start"] = start;
                reply["chunk"]["length"] = length;
                reply["chunk"]["data"] = Convert.ToBase64String(buf);
                SendReply(reply);
            }
            image = null;

            return this;
        }

        private Scan InitTwain(JObject request, JObject reply)
        {
            log.Debug("processing 'init_twain' action");

            if (twain != null) return ReportException(request, reply, "twain already initialized", "twain already initialized");
            
            twainException = null;
            form.TopMost = true;
            form.Invoke((MethodInvoker)delegate
                {
                    try
                    {
                        log.Debug("form delegate: create twain");
                        twain = new Twain(hook);

                        log.Debug("form delegate: set twain transfer image delegate");
                        twain.TransferImage += delegate(Object sender, TransferImageEventArgs args)
                        {
                            log.Debug("twain transfer image delegate");
                            if (args.Image != null)
                            {
                                imageUuid = System.Guid.NewGuid().ToString();
                                image = new System.IO.MemoryStream();
                                // !!!! TODO !!!! add here parameters that allow user to choose image format ////
                                ///!!!! TODO !!!! and format properties using imageAcquirerequest ////
                                args.Image.Save(image, System.Drawing.Imaging.ImageFormat.Jpeg);
                                imageType = "image/jpeg";
                                log.Debug("image transfer complete");
                            }
                            else
                            {
                                log.Debug("no image to transfer");
                            }
                            imageTransferred.Set();

                        };
                        log.Debug("form delegate: set twain scanning complete delegate");
                        twain.ScanningComplete += delegate
                        {
                            log.Debug("scanning complete delegate");
                            imageAcquired.Set();
                        };

                        log.Debug("form delegate: finished");
                    }
                    catch (Exception ex)
                    {
                        log.Error("twain exception", ex);
                        twainException = ex;
                    }
                });
            if (twainException != null) return ReportException(request, reply, twainException.Message, twainException.ToString());
            reply.Add("ok", true);
            
            return SendReply(reply);
        }

        private Scan ListSources(JObject request, JObject reply)
        {
            log.Debug("processing 'list_sources' action");
            if (twain == null) return ReportException(request, reply, "twain not initialized", "use 'init_twain' first");

            twainException = null;
            form.Invoke((MethodInvoker)delegate
            {
                try
                {
                    log.Debug("form delegate: list twain sources");
                    reply.Add("twain", new JObject(new JProperty("sources", new JArray(from source in twain.SourceNames select new JValue(source)))));
                    log.Debug("form delegate: finished");
                }
                catch (Exception ex)
                {
                    log.Error("twain exception", ex);
                    twainException = ex;
                }
            });
            if (twainException != null) return ReportException(request, reply, twainException.Message, twainException.ToString());
            reply.Add("ok", true);

            return SendReply(reply);
        }

        private Scan SelectSource(JObject request, JObject reply)
        {
            log.Debug("processing 'select_source' action");
            if (twain == null) return ReportException(request, reply, "twain not initialized", "use 'init_twain' first");

            twainException = null;
            form.TopMost = true;
            form.Invoke((MethodInvoker)delegate
            {
                try
                {
                    log.Debug("form delegate: bring form to the front");
                    form.BringToFront();
                    log.Debug("form delegate: select twain source");
                    twain.SelectSource();
                    log.Debug("form delegate: finished");
                }
                catch (Exception ex)
                {
                    log.Error("twain exception", ex);
                    twainException = ex;
                }
            });
            if (twainException != null) return ReportException(request, reply, twainException.Message, twainException.ToString());
            reply.Add("ok", true);
            
            return SendReply(reply);
        }

        private Scan AcquireImage(JObject request, JObject reply)
        {
            log.Debug("processing 'acquire_image' action");
            if (twain == null) return ReportException(request, reply, "twain not initialized", "use 'init_twain' first");

            log.Debug("save image acquire request");
            imageAcquireRequest = new JObject(request);

            log.Debug("clear twain exception");
            twainException = null;

            log.Debug("reset acquired image");
            image = null;

            log.Debug("parse scan settings");
            ScanSettings settings;
            try
            {
                settings = ParseScanSettings(request);
            }
            catch (Exception ex)
            {
                log.Error("parse settings exception", ex);
                return ReportException(request, reply, twainException.Message, twainException.ToString());
            }
            
            log.Debug("start scanning");
            imageAcquired.Reset();
            form.Invoke((MethodInvoker)delegate
            {
                try
                {
                    log.Debug("form delegate: start scanning");
                    twain.StartScanning(settings);
                    log.Debug("form delegate: finished");
                }
                catch (Exception ex)
                {
                    twainException = ex;
                    imageAcquired.Set();
                }
            });

            log.Debug("wait for image acquired event");
            imageAcquired.WaitOne();
            log.Debug("image acquired");

            if (twainException != null) return ReportException(request, reply, twainException.Message, twainException.ToString());
            if (image == null) return ReportException(request, reply, "no image acquired", "scanning was cancelled or scanning error");
            reply.Add("image", new JObject(
                new JProperty("uuid", imageUuid),
                new JProperty("type", imageType),
                new JProperty("size", image.Length)));
            reply.Add("ok", true);

            return SendReply(reply);
        }

        private ScanSettings ParseScanSettings(JObject request)
        {
            log.Debug("create default scan settings");
            ScanSettings settings = new ScanSettings();
            settings.UseDocumentFeeder = false;
            settings.ShowTwainUI = true;
            settings.ShowProgressIndicatorUI = true;
            settings.UseDuplex = false;
            settings.Resolution = ResolutionSettings.ColourPhotocopier;
            settings.Area = null;
            settings.ShouldTransferAllPages = true;
            settings.Rotation = new RotationSettings()
            {
                AutomaticRotate = false,
                AutomaticBorderDetection = false
            };

            if (request["settings"] != null)
            {
                log.Debug("parse request scan settings");
                if (request["settings"]["UseDocumentFeeder"] != null) settings.UseDocumentFeeder = (bool)request["settings"]["UseDocumentFeeder"];
                if (request["settings"]["ShowTwainUI"] != null) settings.ShowTwainUI = (bool)request["settings"]["ShowTwainUI"];
                if (request["settings"]["ShowProgressIndicatorUI"] != null) settings.ShowProgressIndicatorUI = (bool)request["settings"]["ShowProgressIndicatorUI"];
                if (request["settings"]["UseDuplex"] != null) settings.UseDuplex = (bool)request["settings"]["UseDuplex"];
                if (request["settings"]["ShouldTransferAllPages"] != null) settings.ShouldTransferAllPages = (bool)request["settings"]["ShouldTransferAllPages"];

                if (request["settings"]["Resolution"] != null)
                {
                    if ((string)request["settings"]["Resolution"] == "Fax") settings.Resolution = ResolutionSettings.Fax;
                    else if ((string)request["settings"]["Resolution"] == "Photocopier") settings.Resolution = ResolutionSettings.Photocopier;
                    else if ((string)request["settings"]["Resolution"] == "ColourPhotocopier") settings.Resolution = ResolutionSettings.ColourPhotocopier;
                    else
                    {
                        settings.Resolution = new ResolutionSettings();
                        if (request["settings"]["Resolution"]["Dpi"] != null) settings.Resolution.Dpi = (int)request["settings"]["Resolution"]["Dpi"];
                        if (request["settings"]["Resolution"]["ColourSetting"] != null)
                        {
                            if ((string)request["settings"]["Resolution"]["ColourSetting"] == "BlackAndWhite") settings.Resolution.ColourSetting = ColourSetting.BlackAndWhite;
                            else if ((string)request["settings"]["Resolution"]["ColourSetting"] == "Colour") settings.Resolution.ColourSetting = ColourSetting.Colour;
                            else if ((string)request["settings"]["Resolution"]["ColourSetting"] == "GreyScale") settings.Resolution.ColourSetting = ColourSetting.GreyScale;
                            else throw new ScanSettingsException("Invalid Resolution.ColourSetting setting.");
                        }
                    }
                }

                if (request["settings"]["Area"] != null)
                {
                    if (request["settings"]["Area"]["units"] == null) throw new ScanSettingsException("Units setting is required in Area.");
                    if (request["settings"]["Area"]["top"] == null) throw new ScanSettingsException("Top setting is required in Area.");
                    if (request["settings"]["Area"]["left"] == null) throw new ScanSettingsException("Left setting is required in Area.");
                    if (request["settings"]["Area"]["bottom"] == null) throw new ScanSettingsException("Bottom setting is required in Area.");
                    if (request["settings"]["Area"]["right"] == null) throw new ScanSettingsException("Right setting is required in Area.");

                    Units units;
                    if ((string)request["settings"]["Area"]["units"] == "Centimeters") units = Units.Centimeters;
                    else if ((string)request["settings"]["Area"]["units"] == "Inches") units = Units.Inches;
                    else if ((string)request["settings"]["Area"]["units"] == "Millimeters") units = Units.Millimeters;
                    else if ((string)request["settings"]["Area"]["units"] == "Picas") units = Units.Picas;
                    else if ((string)request["settings"]["Area"]["units"] == "Pixels") units = Units.Pixels;
                    else if ((string)request["settings"]["Area"]["units"] == "Points") units = Units.Points;
                    else if ((string)request["settings"]["Area"]["units"] == "Twips") units = Units.Twips;
                    else throw new ScanSettingsException("Invalid Area.units setting.");

                    float top, left, bottom, right;
                    top = (float)request["settings"]["Area"]["top"];
                    left = (float)request["settings"]["Area"]["left"];
                    bottom = (float)request["settings"]["Area"]["bottom"];
                    right = (float)request["settings"]["Area"]["right"];

                    settings.Area = new AreaSettings(units, top, left, bottom, right);
                }

                if (request["settings"]["Rotation"] != null)
                {
                    settings.Rotation = new RotationSettings();
                    if (request["settings"]["Rotation"]["AutomaticBorderDetection"] != null) settings.Rotation.AutomaticBorderDetection = (bool)request["settings"]["Rotation"]["AutomaticBorderDetection"];
                    if (request["settings"]["Rotation"]["AutomaticDeskew"] != null) settings.Rotation.AutomaticDeskew = (bool)request["settings"]["Rotation"]["AutomaticDeskew"];
                    if (request["settings"]["Rotation"]["AutomaticRotate"] != null) settings.Rotation.AutomaticRotate = (bool)request["settings"]["Rotation"]["AutomaticRotate"];
                    if (request["settings"]["Rotation"]["FlipSideRotation"] != null)
                    {
                        if ((string)request["settings"]["Rotation"]["FlipSideRotation"] == "Book") settings.Rotation.FlipSideRotation = FlipRotation.Book;
                        else if ((string)request["settings"]["Rotation"]["FlipSideRotation"] == "FanFold") settings.Rotation.FlipSideRotation = FlipRotation.FanFold;
                        else throw new ScanSettingsException("Invalid Rotation.FlipSideRotation setting.");
                    }
                }
            }

            return settings;
        }

        private Scan NoActionSpecified(JObject request, JObject reply)
        {
            log.Debug("no action specified");
            return ReportException(request, reply, "no action specified", "send {\"action\":\"list_actions\"} to get list of available actions");
        }

        private Scan InvalidActionSpecified(JObject request, JObject reply)
        {
            log.Debug("invalid action specified");
            return ReportException(request, reply, "invalid action specified", "send {\"action\":\"list_actions\"} to get list of available actions");
        }

        private Scan ReportException(JObject request, JObject reply, string message, string comment = null)
        {
            log.Debug("reporting exception");
            reply.Add("request", request);
            reply.Add("exception", message);
            if (comment != null) reply.Add("comment", comment);
            reply.Add("ok", false);
            return SendReply(reply);
        }

        private Scan SendReply(JObject reply)
        {
            log.DebugFormat("send outgoing reply\n{0}", reply.ToString());

            port.Write(reply.ToString());

            return this;
        }

        public Scan Stop()
        {
            log.Debug("stop scan host");
            Application.Exit();
            stop.Set();
            return this;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamCoder
{
    public class StreamCoder : Stream
    {
        string _transcodedFile;
        string _sourceFile;
        string _tempFile;
        string _currentFile;

        private Thread _thread;
        private ManualResetEvent _reset;
        private Mutex _mutex;
        FileStream _fileStream;
        private Process _process;
        private readonly TargetFormats _format;

        private bool TranscodingComplete
        {
            get { return !File.Exists(_tempFile); }
        }
        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return TranscodingComplete; } //can only seek up to the end of the current file size, seeking past the end it waits for the file to catch up or end
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get
            {
                if (TranscodingComplete)
                {

                    return RealLength;
                }
                else
                {
                    return -1;
                }

            }
        }
        private long RealLength
        {
            get
            {
                return _fileStream?.Length ?? -1;
            }
        }

        public override long Position
        {
            get { return _fileStream?.Position ?? 0; }

            set
            {
                if (_sourceFile != null) { _fileStream.Position = value; }
            }
        }

        public override void Flush()
        {
            _fileStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return WaitForDataOrClose(Position + count, () => _fileStream.Read(buffer, offset, count));
        }

        private T WaitForDataOrClose<T>(long targetLength, Func<T> run)
        {
            while (RealLength < targetLength && !TranscodingComplete)
            {
                Thread.Sleep(5);
            }

            return Wait(run);
        }

        private T Wait<T>(Func<T> run)
        {
            _mutex.WaitOne();
            var result = run();

            _mutex.ReleaseMutex();

            return result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {

            if (origin == SeekOrigin.Current)
            {
                return WaitForDataOrClose(Position + offset, () => _fileStream.Seek(offset, origin));
            }
            else
            {
                return WaitForDataOrClose(offset, () => _fileStream.Seek(offset, origin));
            }
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        public string CalculateMD5Hash(string input)
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }
        public StreamCoder(string file, TargetFormats format = TargetFormats.MP4)
        {
            //are we in transcoding mode or not?

            _sourceFile = Path.GetFullPath(file);
            _transcodedFile = _sourceFile + "." + format.ToString().ToLower();
            _tempFile = _transcodedFile + ".tran";
            _format = format;


            _mutex = new Mutex(false, CalculateMD5Hash("394BCAFD-2284-4829-B105-AE310552BDCE # " + _sourceFile));

            if (File.Exists(_transcodedFile))
            {
                if (!File.Exists(_tempFile))
                {
                    //all the work is already done
                    _fileStream = File.Open(_transcodedFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    //all setup
                    return;
                }
            }

            if (!File.Exists(file))
            {
                throw new IOException($"Cannot find file with name '{file}'");
            }
            _reset = new ManualResetEvent(false);

            //we need to spin up a transcoder and 
            //Guid used as a namespace to prevent other things clashing

            _thread = new Thread(Worker) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            _reset.WaitOne();

            //we always just open the transcoded file
            _fileStream = File.Open(_transcodedFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            //file streams all setup
        }


        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (_mutex != null)
            {
                _mutex.WaitOne();

                if (_process != null)
                {
                    _process.Kill();
                    File.Delete(_transcodedFile);
                    File.Delete(_tempFile);
                }
                //can kill transcoder
                this._thread?.Join();
                _fileStream.Dispose();
                _mutex.ReleaseMutex();
                _mutex.Close();
                _mutex = null;
            }
        }


        public void Worker()
        {
            _mutex.WaitOne();

            //transcode now complete
            if (!File.Exists(_transcodedFile))
            {
                _process = Process.Start(new ProcessStartInfo("ffmpeg.exe", " -i \"" + _sourceFile + "\" -s 800x450 -movflags faststart \"" + _transcodedFile + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    // RedirectStandardOutput = true,
                    //RedirectStandardError= true
                });

                File.Create(_tempFile).Close();
                WaitForTranscodeToBegin();
                _reset.Set();//signal to the caller we can now continue

                _mutex.ReleaseMutex();

                _process.WaitForExit();
                _mutex.WaitOne();
                _process = null;
                File.Delete(_tempFile);
            }
            else
            {
                _reset.Set();
            }

            _mutex.ReleaseMutex();
        }

        public void WaitForTranscodeToBegin()
        {
            while (!File.Exists(_transcodedFile) && !_process.WaitForExit(0))
            {
                Thread.Sleep(5);
            }
        }

        public void WaitForTranscodeToEnd()
        {
            while (!File.Exists(_transcodedFile))
            {
                Thread.Sleep(5);
            }
        }

    }

}

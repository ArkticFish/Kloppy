using System.Collections.Concurrent;

namespace Kloppy
{
    public class FileCopyManager
    {
        private readonly int bufferSize;
        private readonly string destinationDir;
        private readonly int totalFiles;
        private int filesCopied;

        // Current buffer for accumulating small files
        private List<(string RelativePath, byte[] Data)> currentBuffer;
        private long currentBufferSize;

        // Blocking collection to hold buffers to write, with bounded capacity
        private BlockingCollection<List<(string RelativePath, byte[] Data)>> bufferQueue;

        // Task for the single write worker
        private Task writeTask;

        // Reference to the shared progress message variable
        private static readonly object consoleLock = new();

        public FileCopyManager(int bufferSizeInBytes, string destinationDir, int totalFiles, int maxBuffersInMemory)
        {
            bufferSize = bufferSizeInBytes;
            this.destinationDir = destinationDir;
            this.totalFiles = totalFiles;
            this.filesCopied = 0;

            currentBuffer = [];
            currentBufferSize = 0;

            // Initialize buffer queue with bounded capacity
            bufferQueue = new BlockingCollection<List<(string RelativePath, byte[] Data)>>(boundedCapacity: maxBuffersInMemory);

            // Start the single write task
            writeTask = Task.Run(() => WriteWorker());
        }

        public async Task CopyFileAsync(string sourceFile)
        {
            FileInfo fileInfo = new(sourceFile);
            string relativePath = Path.GetFileName(sourceFile);

            if (fileInfo.Length > bufferSize)
            {
                // Handle large file separately
                string destinationFile = Path.Combine(destinationDir, relativePath);
                await CopyLargeFileAsync(sourceFile, destinationFile);
            }
            else
            {
                // Read small file into buffer
                byte[] fileData = await File.ReadAllBytesAsync(sourceFile);
                currentBuffer.Add((relativePath, fileData));
                currentBufferSize += fileData.Length;

                // Flush buffer if it exceeds the size limit
                if (currentBufferSize >= bufferSize)
                {
                    // Swap buffers
                    var bufferToFlush = currentBuffer;
                    currentBuffer = new List<(string RelativePath, byte[] Data)>();
                    currentBufferSize = 0;

                    // Add the buffer to the queue (will block if the queue is full)
                    bufferQueue.Add(bufferToFlush);
                }
            }
        }

        public async Task CopyDirectoryAsync(string sourceDir)
        {

            string parentDirectory = Path.GetDirectoryName(sourceDir.TrimEnd(Path.DirectorySeparatorChar));

            // Get all files in the directory and subdirectories
            var sourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);

            foreach (var filePath in sourceFiles)
            {
                string relativePath = Path.GetRelativePath(parentDirectory, filePath);

                FileInfo fileInfo = new(filePath);

                if (fileInfo.Length > bufferSize)
                {
                    // Handle large file separately
                    string destinationFile = Path.Combine(destinationDir, relativePath);
                    await CopyLargeFileAsync(filePath, destinationFile);
                }
                else
                {
                    // Read small file into buffer
                    byte[] fileData = await File.ReadAllBytesAsync(filePath);
                    currentBuffer.Add((relativePath, fileData));
                    currentBufferSize += fileData.Length;

                    // Flush buffer if it exceeds the size limit
                    if (currentBufferSize >= bufferSize)
                    {
                        // Swap buffers
                        var bufferToFlush = currentBuffer;
                        currentBuffer = [];
                        currentBufferSize = 0;

                        // Add the buffer to the queue (will block if the queue is full)
                        bufferQueue.Add(bufferToFlush);
                    }
                }
            }
        }

        private async Task CopyLargeFileAsync(string sourceFile, string destinationFile)
        {
            // Ensure the destination directory exists
            string destinationDirPath = Path.GetDirectoryName(destinationFile);
            if (!Directory.Exists(destinationDirPath))
            {
                Directory.CreateDirectory(destinationDirPath);
            }

            const int copyBufferSize = 10 * 1024 * 1024; // 4MB buffer

            using (FileStream sourceStream = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, copyBufferSize, useAsync: true))
            using (FileStream destinationStream = new(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, copyBufferSize, useAsync: true))
            {
                byte[] buffer = new byte[copyBufferSize];
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await destinationStream.WriteAsync(buffer, 0, bytesRead);
                }
            }

            // Update progress after writing the large file
            UpdateProgress();
        }

        private void UpdateProgresssss()
        {
            lock (consoleLock)
            {
                filesCopied++;
                Program.progressMessage = $"Copying files... {filesCopied}/{totalFiles} files copied.";
            }
        }

        private void UpdateProgress()
        {
            lock (consoleLock)
            {
                // Increment the number of files copied.
                filesCopied++;

                int progressPercentage = filesCopied * 100 / totalFiles;
                int progressBarWidth = 30;
                int progressFilled = progressPercentage * progressBarWidth / 100;

                // Build the progress bar
                string progressBar = new string('=', progressFilled) + new string(' ', progressBarWidth - progressFilled);

                // Display the progress message and bar
                Program.progressMessage = $"Copying files... [{progressBar}] {progressPercentage}% ({filesCopied}/{totalFiles})";
            }
        }

        private async Task WriteWorker()
        {
            foreach (var bufferToWrite in bufferQueue.GetConsumingEnumerable())
            {
                await WriteBufferToDiskAsync(bufferToWrite);
            }
        }

        private async Task WriteBufferToDiskAsync(List<(string RelativePath, byte[] Data)> bufferToWrite)
        {
            foreach (var (relativePath, fileData) in bufferToWrite)
            {
                string destinationFile = Path.Combine(destinationDir, relativePath);

                // Ensure the destination directory exists
                string destinationDirPath = Path.GetDirectoryName(destinationFile);
                if (!Directory.Exists(destinationDirPath))
                {
                    Directory.CreateDirectory(destinationDirPath);
                }

                await File.WriteAllBytesAsync(destinationFile, fileData);

                // Update progress after writing each file
                UpdateProgress();
            }
        }

        public async Task FlushBuffersAsync()
        {
            // Flush the current buffer if it has data
            if (currentBufferSize > 0)
            {
                var bufferToFlush = currentBuffer;
                currentBuffer = [];
                currentBufferSize = 0;

                bufferQueue.Add(bufferToFlush);
            }

            // Signal that no more buffers will be added
            bufferQueue.CompleteAdding();

            // Wait for the write worker to finish
            await writeTask;
        }
    }
}

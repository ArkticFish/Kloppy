namespace Kloppy
{
    class Program
    {
        // Lock object for synchronizing console output
        private static readonly object consoleLock = new object();

        // Shared progress message variable
        public static string progressMessage = string.Empty;

        static int Main(string[] args)
        {
            int exitCode = 0;

            var startTime = DateTime.Now;

            // Resize the console window. 4 Lines of text will be displayed at a time.
            Console.WindowWidth = 80;
            Console.WindowHeight = 4;
            Console.BufferWidth = 80;
            Console.BufferHeight = 4;

            // Create a new STA thread
            var staThread = new Thread(() =>
            {
                // Run your async code synchronously
                exitCode = RunAsync(args).GetAwaiter().GetResult();
            });

            // Set the apartment state to STA
            staThread.SetApartmentState(ApartmentState.STA);

            // Start the STA thread
            staThread.Start();
            staThread.Join();

            var endTime = DateTime.Now;

            Console.Clear();
            Console.WriteLine($"Time taken: {endTime - startTime}");
            Console.ReadLine();

            return exitCode;
        }

        static async Task<int> RunAsync(string[] args)
        {
            bool pauseAfterCompletion = false;

            // Check for the --pause argument
            if (args.Contains("--pause"))
            {
                pauseAfterCompletion = true;
                args = args.Where(a => a != "--pause").ToArray(); // Remove --pause from args
            }

            // The program only works with the --paste argument
            if (args.Length == 2 && args[0] == "--paste")
            {
                string destinationDir = args[1];
                int result = await PasteFiles(destinationDir);

                if (pauseAfterCompletion)
                {
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                }

                return result;
            }
            else
            {
                Console.WriteLine("Usage: kloppy.exe --paste \"destination_directory\" [--pause]");
                if (pauseAfterCompletion)
                {
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                }
                return 1;
            }
        }

        static async Task<int> PasteFiles(string destinationDir)
        {
            try
            {
                // Use ClipboardHelper to get file drop list
                var fileDropList = ClipboardHelper.GetFileDropList();
                if (fileDropList == null || fileDropList.Count == 0)
                {
                    Console.WriteLine("Clipboard does not contain any files or folders to paste.");
                    return 1;
                }

                List<string> sourcePaths = fileDropList.Cast<string>().ToList();

                // Ensure the destination directory exists
                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Collect all files to copy
                List<string> allFilesToCopy = [];

                foreach (var sourcePath in sourcePaths)
                {
                    if (File.Exists(sourcePath))
                    {
                        allFilesToCopy.Add(sourcePath);
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        // Get all files in the directory and subdirectories
                        var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories).ToList();
                        allFilesToCopy.AddRange(files);
                    }
                    else
                    {
                        Console.WriteLine($"Source path does not exist: {sourcePath}");
                    }
                }

                // Initialize progress tracking
                int totalFiles = allFilesToCopy.Count;
                int filesCopied = 0;

                // Create a cancellation token source for the console update task
                bool isBusy = true;

                // Start the console update task
                Task consoleUpdateTask = Task.Run(async () =>
                {
                    while (isBusy)
                    {
                        lock (consoleLock)
                        {
                            Console.Write($"\r{progressMessage}");
                        }
                        await Task.Delay(100);
                    }
                });

                var fileCopyManager = new FileCopyManager(
                    bufferSizeInBytes: 100 * 1024 * 1024,
                    destinationDir: destinationDir,
                    totalFiles: totalFiles,
                    maxBuffersInMemory: 5
                );

                // Copy files with progress
                foreach (var sourcePath in sourcePaths)
                {
                    if (File.Exists(sourcePath))
                    {
                        // Source is a file
                        await fileCopyManager.CopyFileAsync(sourcePath);
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        // Source is a directory
                        await fileCopyManager.CopyDirectoryAsync(sourcePath);
                    }
                }

                // Ensure any remaining buffered data is written to disk
                await fileCopyManager.FlushBuffersAsync();

                // Cancel the console update task
                isBusy = false;
                await consoleUpdateTask;

                // Move to the next line after progress is complete
                Console.WriteLine();

                Console.WriteLine("All files and folders pasted successfully.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError during paste operation: {ex.Message}");
                return 1;
            }
        }
    }
}

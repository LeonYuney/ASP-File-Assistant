using System.Security.Cryptography;
using System.Text.Json;

namespace FileSupport_PracticKarevo
{
    class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
        // Поддерживаемые расширения архивов (нижний регистр)
        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2"
        };

        static void Main()
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.DarkGreen;

            string[] aspArt = [
            "   █████╗ ███████╗██████╗     ",
            "  ██╔══██╗██╔════╝██╔══██╗    ",
            "  ███████║███████╗██████╔╝    ",
            "  ██╔══██║╚════██║██╔═══╝     ",
            "  ██║  ██║███████║██║         ",
            "  ╚═╝  ╚═╝╚══════╝╚═╝         "
        ];

            // Мерцающая отрисовка
            for (int i = 0; i < aspArt.Length; i++)
            {
                Console.WriteLine(aspArt[i]);
                Thread.Sleep(80);
            }

            Thread.Sleep(1000);

            // Очищаем консоль и восстанавливаем курсор
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Clear();

            string directory = GetValidDirectory();
            Console.WriteLine($"\nАнализируем: {directory}\n");

            // Получаем все файлы рекурсивно
            var allFiles = GetAllFiles(directory);
            if (allFiles.Count == 0)
            {
                Console.WriteLine("Файлы не найдены.");
                return;
            }

            // Фильтруем
            var largeFiles = new List<FileInfo>();
            var archiveFiles = new List<FileInfo>();
            var duplicates = new List<FileInfo>();

            for (int i = 0; i < allFiles.Count; i++)
            {
                var file = allFiles[i];
                try
                {
                    if (file.Length > 100 * 1024 * 1024) // >100 МБ
                        largeFiles.Add(file);

                    string ext = Path.GetExtension(file.Name);
                    if (ArchiveExtensions.Contains(ext))
                        archiveFiles.Add(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Пропущен файл (ошибка): {file.FullName} — {ex.Message}");
                }
            }

            // Пересечения: большие архивы
            duplicates = largeFiles.Intersect(archiveFiles).ToList();

            if (duplicates.Count > 0)
            {
                Console.WriteLine("Найдены большие архивы (входят в обе категории):");
                PrintFileTable(duplicates);
                if (AskYesNo("Хотите удалить эти файлы?"))
                {
                    var duplicatesList = duplicates.ToList();
                    for (int i = 0; i < duplicatesList.Count; i++)
                    {
                        var file = duplicatesList[i];
                        if (ConfirmDelete(file))
                        {
                            DeleteFile(file);
                        }
                    }
                }
            }

            // Объединяем все файлы из обеих категорий и сортируем по размеру (убывание)
            var combined = largeFiles.Union(archiveFiles).Distinct().OrderByDescending(f => f.Length).ToList();

            if (combined.Count == 0)
            {
                Console.WriteLine("\nНет файлов, подходящих под критерии.");
                return;
            }

            Console.WriteLine("\nВсе файлы (>100 МБ или архивы), отсортированы по размеру:");
            PrintFileTable(combined);

            // Цикл удаления
            var logEntries = new List<LogEntry>();
            for (int i = 0; i < combined.Count; i++)
            {
                var file = combined[i];
                Console.Write($"\nУдалить файл {file.Name}? (1 — да, 0 — выход, Enter — пропустить): ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (input == "0")
                    break;

                if (input == "1")
                {
                    if (ConfirmDelete(file))
                    {
                        if (DeleteFile(file))
                        {
                            string hash = ComputeFileHash(file.FullName);
                            logEntries.Add(new LogEntry
                            {
                                Path = file.FullName,
                                Size = file.Length,
                                DeletedAt = DateTime.UtcNow.ToString(""),
                                Hash = hash
                            });
                        }
                    }
                }
            }
            // Сохраняем лог
            if (logEntries.Count > 0)
            {
                string json = JsonSerializer.Serialize(logEntries, JsonOptions);
                File.WriteAllText(LogPath, json, System.Text.Encoding.UTF8);
                Console.WriteLine($"\nЛог удалений сохранён: {LogPath}");
            }

            Console.WriteLine("\nПрограмма завершена.");
        }

        // Путь к логу
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "file_assistant.log");

        static string GetValidDirectory()
        {
            while (true)
            {
                Console.Write("Введите путь к директории для анализа: ");
                string input = Console.ReadLine()?.Trim() ?? "";

                // Пропускаем пустой ввод
                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("Путь не может быть пустым. Попробуйте снова.");
                    continue;
                }

                // Проверяем, существует ли директория
                if (!Directory.Exists(input))
                {
                    Console.WriteLine("Указанная директория не существует. Проверьте путь и повторите ввод.");
                    continue;
                }

                // Проверяем, есть ли у программы права на чтение директории
                try
                {
                    // Попытка прочитать файлы — если получится, значит, права есть
                    string[] files = Directory.GetFiles(input);
                    // Нам не нужны сами файлы — только факт, что чтение удалось
                }
                catch (UnauthorizedAccessException)
                {
                    Console.WriteLine("Нет прав на чтение этой директории. Выберите другую папку.");
                    continue;
                }

                // Если всё прошло успешно — возвращаем полный путь
                return Path.GetFullPath(input);
            }
        }

        static List<FileInfo> GetAllFiles(string root)
        {
            var files = new List<FileInfo>();
            try
            {
                var allPaths = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                for (int i = 0; i < allPaths.Length; i++)
                {
                    string path = allPaths[i];
                    try
                    {
                        files.Add(new FileInfo(path));
                    }
                    catch (Exception fileEx)
                    {
                        Console.WriteLine($"Не удалось создать FileInfo для: {path} — {fileEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сканировании: {ex.Message}");
            }
            return files;
        }

        static void PrintFileTable(List<FileInfo> files)
        {
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"{"№",-3} {"Имя",-30} {"Размер",-12} {"Дата создания",-20}");
            Console.WriteLine(new string('-', 80));

            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string size = FormatFileSize(f.Length);
                string date = f.CreationTime.ToString("yyyy-MM-dd HH:mm");
                string name = f.Name.Length > 28 ? f.Name[..25] + "..." : f.Name;
                Console.WriteLine($"{i + 1,-3} {name,-30} {size,-12} {date,-20}");
            }
            Console.WriteLine(new string('-', 80));
        }

        static string FormatFileSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
            if (bytes >= 1_024) return $"{bytes / 1_024.0:F2} KB";
            return $"{bytes} B";
        }

        static bool AskYesNo(string question)
        {
            while (true)
            {
                Console.Write($"{question} (Y/N): ");
                string? answer = Console.ReadLine()?.Trim().ToLower() ?? "";
                if (answer == "y" || answer == "yes" || answer == "ye")
                {
                    return true;
                }
                if (answer == "n" || answer == "no" || answer == "now")
                {
                    return false;
                }
            }
        }

        static bool ConfirmDelete(FileInfo file)
        {
            Console.WriteLine($"\nВнимание! Вы собираетесь удалить:");
            Console.WriteLine($"   Имя: {file.Name}");
            Console.WriteLine($"   Путь: {file.FullName}");
            Console.WriteLine($"   Размер: {FormatFileSize(file.Length)}");
            Console.Write("\nПовторите имя файла для подтверждения (или Enter для отмены): ");
            string? input = Console.ReadLine()?.Trim() ?? "";
            return input == file.Name;
        }

        static bool DeleteFile(FileInfo file)
        {
            try
            {
                File.Delete(file.FullName);
                Console.WriteLine($"Удалено: {file.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось удалить: {ex.Message}");
                return false;
            }
        }

        static string ComputeFileHash(string filePath)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return "hash-unavailable";
            }
        }
    }

    // Структура для лога
    class LogEntry
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string DeletedAt { get; set; } = "";
        public string Hash { get; set; } = "";
    }
}
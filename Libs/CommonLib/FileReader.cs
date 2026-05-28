using System;
using System.IO;
using System.Text;

namespace CommonLib
{
    /// <summary>
    /// Утилиты для чтения текстовых файлов с поддержкой
    /// автоматического определения кодировки.
    /// </summary>
    public static class FileReader
    {
        /// <summary>
        /// Читает все строки текстового файла.
        /// Если задано несколько кодировок — пробует каждую по порядку
        /// и выбирает первую, при чтении которой в тексте нет replacement-символов.
        /// </summary>
        /// <param name="filePath">Путь к файлу</param>
        /// <param name="encodings">Кодировки для попытки (по порядку)</param>
        /// <param name="skipEncodings">Пропустить словарь кодировок и читать как есть</param>
        /// <returns>Массив строк</returns>
        public static string[] ReadLines(string filePath, Encoding[] encodings, bool skipEncodings = false)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Файл не найден: {filePath}");

            // Если кодировка одна — читаем напрямую
            if (encodings == null || encodings.Length == 0 || skipEncodings)
            {
                string content = File.ReadAllText(filePath);
                return SplitLines(content);
            }

            // Пробуем несколько кодировок
            foreach (var encoding in encodings)
            {
                string content = File.ReadAllText(filePath, encoding);

                if (!content.Contains("\uFFFD"))
                {
                    return SplitLines(content);
                }
            }

            // Fallback — первая (основная) кодировка
            string fallbackContent = File.ReadAllText(filePath, encodings[0]);
            return SplitLines(fallbackContent);
        }

        /// <summary>
        /// Разбивает текст на строки, поддерживая \r\n, \r и \n.
        /// </summary>
        private static string[] SplitLines(string content)
        {
            return content.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
        }

        public static string RewriteAndReadFileToUTF8(string inputFile, bool throwError = false)
        {
            var sourceEncoding = Encoding.GetEncoding(1251);
            var targetEncoding = new UTF8Encoding(false);
            if (!File.Exists(inputFile))
            {
                if (throwError)
                    throw new FileNotFoundException(inputFile);
                else
                    return string.Empty;
            }
            string fileText = File.ReadAllText(inputFile, sourceEncoding);
            File.WriteAllText(inputFile, fileText, targetEncoding);
            return fileText;
        }
    }
}

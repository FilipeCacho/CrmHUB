using System;
using System.IO;
using System.Reflection;
using OfficeOpenXml;


    public static class ExcelTemplateManager
    {
        private const string EmbeddedResourceName = "RitmsHub.dataCenter.xlsx";
        private const string FileName = "dataCenter.xlsx";

        public static string GetExcelFilePath()
        {
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"../../../", FileName));
        }

        public static bool ExcelFileExists()
        {
            return File.Exists(GetExcelFilePath());
        }

        public static bool CheckAndCreateExcelFile()
        {
            string filePath = GetExcelFilePath();

            if (ExcelFileExists())
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"Excel file found at: {filePath}");
                Console.ForegroundColor = ConsoleColor.Blue;
                return true;
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Excel file not found");
            Console.WriteLine($"Creating file at: {filePath}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nThis mean you need to insert your username & password in the login sheet for each of the envs you want to access\n");
            Console.WriteLine("You only need to do this once, if you keep the executable in the same folder as the excel file the program will \nread the file and it's login details if it exists\n");
            Console.ResetColor();
            Console.WriteLine("Press any key to continue");
            Console.ReadKey();

            try
            {
                ExtractExcelTemplate();
                Console.WriteLine("Excel file created successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create Excel file: {ex.Message}");
                return false;
            }
        }

        public static void ExtractExcelTemplate()
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException($"Excel template resource not found. Looked for: {EmbeddedResourceName}");
                }

                using (var file = new FileStream(GetExcelFilePath(), FileMode.Create, FileAccess.Write))
                {
                    resource.CopyTo(file);
                }
            }
        }

        public static bool ValidateExcelFile()
        {
            try
            {
                using (var package = new ExcelPackage(new FileInfo(GetExcelFilePath())))
                {
                    // Check if required worksheets exist
                    var requiredWorksheets = new[] { "Login", "Create Teams", "Assign Teams" };
                    foreach (var worksheet in requiredWorksheets)
                    {
                        if (package.Workbook.Worksheets[worksheet] == null)
                        {
                            Console.WriteLine($"Required worksheet '{worksheet}' is missing.");
                            return false;
                        }
                    }


                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error validating Excel file: {ex.Message}");
                return false;
            }
        }

        public static void ListEmbeddedResources()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();
            Console.WriteLine("Embedded resources:");
            foreach (var name in resourceNames)
            {
                Console.WriteLine(name);
            }
        }
    }

using OfficeOpenXml;

public class HardcodedExcelAdjuster
{
    public void ProcessExcelFile()
    {
        var thread = new Thread(() =>
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Excel Files|*.xlsx;*.xls",
                    Title = "Select an Excel File"
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    Console.WriteLine($"Selected file: {openFileDialog.FileName}");
                    Console.WriteLine("Processing file...");

                    ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                    using (var package = new ExcelPackage(new FileInfo(openFileDialog.FileName)))
                    {
                        var worksheet = package.Workbook.Worksheets[0];
                        var rowCount = worksheet.Dimension.End.Row;

                        // Calculate the original positions of all datetime columns
                        var columnT = 20;
                        var columnW = 23;
                        var columnX = 24;
                        var columnAD = 30;
                        var columnAE = 31;

                        // Insert new columns from right to left
                        worksheet.InsertColumn(columnAE + 1, 1); // For AE hours
                        worksheet.InsertColumn(columnAD + 1, 1); // For AD hours
                        worksheet.InsertColumn(columnX + 1, 1);  // For X hours
                        worksheet.InsertColumn(columnW + 1, 1);  // For W hours
                        worksheet.InsertColumn(columnT + 1, 1);  // For T hours

                        // Process columns E, I, N (first : removal)
                        ProcessFirstColonRemoval(worksheet, 5, rowCount);  // E
                        ProcessFirstColonRemoval(worksheet, 9, rowCount);  // I
                        ProcessFirstColonRemoval(worksheet, 14, rowCount); // N

                        // Process column S (last : removal)
                        ProcessLastColonRemoval(worksheet, 19, rowCount);

                        // Process datetime columns with adjusted positions
                        ProcessDateTimeColumn(worksheet, columnT, rowCount);     // T
                        ProcessDateTimeColumn(worksheet, columnW + 1, rowCount); // W (shifted by T's new column)
                        ProcessDateTimeColumn(worksheet, columnX + 2, rowCount); // X (shifted by T and W's new columns)
                        ProcessDateTimeColumn(worksheet, columnAD + 3, rowCount); // AD (shifted by T, W, and X's new columns)
                        ProcessDateTimeColumn(worksheet, columnAE + 4, rowCount); // AE (shifted by T, W, X, and AD's new columns)

                        // Process original column V (shifted by T's new column)
                        ProcessColumnV(worksheet, 23, rowCount);

                        // Process column AC (shifted by previous new columns)
                        ProcessFirstColonRemoval(worksheet, 29, rowCount);

                        // Save the modified file
                        var fileInfo = new FileInfo(openFileDialog.FileName);
                        var newFileName = Path.Combine(
                            fileInfo.DirectoryName!,
                            $"{Path.GetFileNameWithoutExtension(fileInfo.Name)}_modified{fileInfo.Extension}"
                        );
                        package.SaveAs(new FileInfo(newFileName));
                        Console.WriteLine($"File processed and saved as: {newFileName}");
                    }
                }
                else
                {
                    Console.WriteLine("No file was selected.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing Excel file: {ex.Message}");
            }

            Console.WriteLine("\nPress any key to return to menu...");
            Console.ReadKey();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private void ProcessFirstColonRemoval(ExcelWorksheet worksheet, int col, int rowCount)
    {
        for (int row = 2; row <= rowCount; row++)
        {
            var cellValue = worksheet.Cells[row, col].Text;
            if (!string.IsNullOrEmpty(cellValue))
            {
                var colonIndex = cellValue.IndexOf(':');
                if (colonIndex >= 0)
                {
                    worksheet.Cells[row, col].Value = cellValue.Substring(0, colonIndex);
                }
            }
        }
    }

    private void ProcessLastColonRemoval(ExcelWorksheet worksheet, int col, int rowCount)
    {
        for (int row = 2; row <= rowCount; row++)
        {
            var cellValue = worksheet.Cells[row, col].Text;
            if (!string.IsNullOrEmpty(cellValue))
            {
                var colonIndex = cellValue.LastIndexOf(':');
                if (colonIndex >= 0)
                {
                    worksheet.Cells[row, col].Value = cellValue.Substring(colonIndex + 1).Trim();
                }
            }
        }
    }

    private void ProcessDateTimeColumn(ExcelWorksheet worksheet, int col, int rowCount)
    {
        var headerText = worksheet.Cells[1, col].Text;
        worksheet.Cells[1, col + 1].Value = $"{headerText}-hour";

        for (int row = 2; row <= rowCount; row++)
        {
            var cellValue = worksheet.Cells[row, col].Text;
            if (!string.IsNullOrEmpty(cellValue) && DateTime.TryParse(cellValue, out DateTime dateTime))
            {
                // Only put the date in the original column
                worksheet.Cells[row, col].Value = dateTime.ToString("yyyy-MM-dd");
                // Put the time in the new column
                worksheet.Cells[row, col + 1].Value = dateTime.ToString("HH:mm:ss");
            }
        }
    }

    private void ProcessColumnV(ExcelWorksheet worksheet, int col, int rowCount)
    {
        for (int row = 2; row <= rowCount; row++)
        {
            var cellValue = worksheet.Cells[row, col].Text;
            if (!string.IsNullOrEmpty(cellValue))
            {
                var lastAtIndex = cellValue.LastIndexOf('@');
                if (lastAtIndex >= 0)
                {
                    // Get everything after the last @
                    var afterAt = cellValue.Substring(lastAtIndex + 1).Trim();

                    // Remove the initial hyphen if present
                    if (afterAt.StartsWith("-"))
                    {
                        afterAt = afterAt.Substring(1).Trim();
                    }

                    // Find first whitespace after priority level
                    var spaceIndex = afterAt.IndexOf(' ');

                    // If space found, take everything before it, otherwise take the whole string
                    var priorityLevel = spaceIndex >= 0 ?
                        afterAt.Substring(0, spaceIndex) :
                        afterAt;

                    worksheet.Cells[row, col].Value = priorityLevel;
                }
            }
        }
    }
}
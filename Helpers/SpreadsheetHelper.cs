using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WordParserCore.Helpers
{
    public static class SpreadsheetHelper
    {
        public static Cell CreateTextCell(string columnName, uint rowIndex, string text)
        {
            Cell cell = new Cell
            {
                CellReference = $"{columnName}{rowIndex}",
                DataType = CellValues.InlineString
            };

            InlineString inlineString = new InlineString();
            Text t = new Text { Text = text ?? string.Empty };
            inlineString.AppendChild(t);
            cell.AppendChild(inlineString);

            return cell;
        }
    }
}
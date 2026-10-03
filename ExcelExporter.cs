using System.IO.Compression;
using System.Text;
using System.Xml;

namespace DualCycleTrader;

public static class ExcelExporter
{
    private const string SpreadsheetNamespace="http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Write(string path,IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var file=File.Create(path);
        Write(file,rows);
    }

    public static void Write(Stream output,IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var zip=new ZipArchive(output,ZipArchiveMode.Create,leaveOpen:true);
        Add(zip,"[Content_Types].xml",writer=>{
            writer.WriteStartElement("Types","http://schemas.openxmlformats.org/package/2006/content-types");
            writer.WriteStartElement("Default"); writer.WriteAttributeString("Extension","rels");
            writer.WriteAttributeString("ContentType","application/vnd.openxmlformats-package.relationships+xml"); writer.WriteEndElement();
            writer.WriteStartElement("Default"); writer.WriteAttributeString("Extension","xml");
            writer.WriteAttributeString("ContentType","application/xml"); writer.WriteEndElement();
            writer.WriteStartElement("Override"); writer.WriteAttributeString("PartName","/xl/workbook.xml");
            writer.WriteAttributeString("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"); writer.WriteEndElement();
            writer.WriteStartElement("Override"); writer.WriteAttributeString("PartName","/xl/worksheets/sheet1.xml");
            writer.WriteAttributeString("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"); writer.WriteEndElement();
            writer.WriteEndElement();
        });
        Add(zip,"_rels/.rels",writer=>{
            writer.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");
            writer.WriteStartElement("Relationship"); writer.WriteAttributeString("Id","rId1");
            writer.WriteAttributeString("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
            writer.WriteAttributeString("Target","xl/workbook.xml"); writer.WriteEndElement();
            writer.WriteEndElement();
        });
        Add(zip,"xl/workbook.xml",writer=>{
            writer.WriteStartElement("workbook",SpreadsheetNamespace);
            writer.WriteStartElement("sheets",SpreadsheetNamespace);
            writer.WriteStartElement("sheet",SpreadsheetNamespace);
            writer.WriteAttributeString("name","目前表格"); writer.WriteAttributeString("sheetId","1");
            writer.WriteAttributeString("r","id","http://schemas.openxmlformats.org/officeDocument/2006/relationships","rId1");
            writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        });
        Add(zip,"xl/_rels/workbook.xml.rels",writer=>{
            writer.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");
            writer.WriteStartElement("Relationship"); writer.WriteAttributeString("Id","rId1");
            writer.WriteAttributeString("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            writer.WriteAttributeString("Target","worksheets/sheet1.xml"); writer.WriteEndElement();
            writer.WriteEndElement();
        });
        Add(zip,"xl/worksheets/sheet1.xml",writer=>{
            writer.WriteStartElement("worksheet",SpreadsheetNamespace);
            writer.WriteStartElement("sheetData",SpreadsheetNamespace);
            for(int rowIndex=0;rowIndex<rows.Count;rowIndex++)
            {
                writer.WriteStartElement("row",SpreadsheetNamespace);
                writer.WriteAttributeString("r",(rowIndex+1).ToString());
                foreach(string value in rows[rowIndex])
                {
                    writer.WriteStartElement("c",SpreadsheetNamespace);
                    writer.WriteAttributeString("t","inlineStr");
                    writer.WriteStartElement("is",SpreadsheetNamespace);
                    writer.WriteStartElement("t",SpreadsheetNamespace);
                    writer.WriteAttributeString("xml","space",null,"preserve");
                    writer.WriteString(string.Concat(value.Where(XmlConvert.IsXmlChar)));
                    writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement(); writer.WriteEndElement();
        });
    }

    private static void Add(ZipArchive zip,string name,Action<XmlWriter> write)
    {
        using var stream=zip.CreateEntry(name,CompressionLevel.Fastest).Open();
        using var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false)});
        write(writer);
    }
}

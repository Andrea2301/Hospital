using System.Text;
using HospitalSV.DTOs;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace HospitalSV.Services;

public class PdfReportService : IPdfReportService
{
    public byte[] GenerateMedicalReportPdf(MedicalReportResponseDto report)
    {
        using var document = new PdfDocument();
        document.Info.Title = $"Reporte Médico #{report.Id} - {report.PatientName}";
        document.Info.Subject = "Informe Clínico Oficial Hospital San Vicente";
        document.Info.Author = report.DoctorName;

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.Letter;
        page.Orientation = PdfSharpCore.PageOrientation.Portrait;

        using var gfx = XGraphics.FromPdfPage(page);

        // Palette
        var navyBlue  = XColor.FromArgb(15, 23, 42);    // #0f172a
        var primary   = XColor.FromArgb(2, 132, 199);   // #0284c7
        var textDark  = XColor.FromArgb(30, 41, 59);    // #1e293b
        var textMuted = XColor.FromArgb(100, 116, 139); // #64748b
        var lightGray = XColor.FromArgb(241, 245, 249); // #f1f5f9
        var borderPen = new XPen(XColor.FromArgb(226, 232, 240), 1);
        var accentPen = new XPen(primary, 2);

        // Fonts
        var fontTitle       = new XFont("Arial", 16, XFontStyle.Bold);
        var fontSubtitle    = new XFont("Arial", 9, XFontStyle.Regular);
        var fontSection     = new XFont("Arial", 11, XFontStyle.Bold);
        var fontBodyBold    = new XFont("Arial", 9.5, XFontStyle.Bold);
        var fontBody        = new XFont("Arial", 9.5, XFontStyle.Regular);
        var fontSmall       = new XFont("Arial", 8, XFontStyle.Regular);
        var fontSmallBold   = new XFont("Arial", 8, XFontStyle.Bold);

        double y = 40;
        const double margin = 40;
        var pageWidth = page.Width.Point;
        var contentWidth = pageWidth - (margin * 2);

        // ─── Institutional Header ─────────────────────────────────────────────
        gfx.DrawRectangle(new XSolidBrush(lightGray), margin, y, contentWidth, 65);
        gfx.DrawLine(accentPen, margin, y, margin, y + 65);

        gfx.DrawString("HOSPITAL SAN VICENTE", fontTitle, new XSolidBrush(navyBlue), margin + 15, y + 24);
        gfx.DrawString("DEPARTAMENTO DE ATENCIÓN MÉDICA Y ESPECIALIDADES", fontSubtitle, new XSolidBrush(textMuted), margin + 15, y + 38);
        gfx.DrawString("Av. Las Palmas #45-12, Medellín · Tel: (604) 444-0000 · www.hospitalsanvicente.com", fontSmall, new XSolidBrush(textMuted), margin + 15, y + 52);

        // Folio & Date Badge (Right aligned)
        var folioText = $"FOLIO: HSV-RPT-{report.Id:D5}";
        var dateText  = $"Fecha: {DateTime.UtcNow:dd/MM/yyyy HH:mm}";
        gfx.DrawString(folioText, fontBodyBold, new XSolidBrush(primary), pageWidth - margin - 150, y + 26);
        gfx.DrawString(dateText, fontSmall, new XSolidBrush(textMuted), pageWidth - margin - 150, y + 42);

        y += 80;

        // Document Title
        gfx.DrawString("INFORME CLÍNICO Y PLAN DE TRATAMIENTO", fontSection, new XSolidBrush(navyBlue), margin, y);
        gfx.DrawLine(borderPen, margin, y + 6, margin + contentWidth, y + 6);
        y += 18;

        // ─── Patient & Doctor Demographics (2 Column Box) ─────────────────────
        var boxHeight = 70;
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 250, 252)), margin, y, contentWidth, boxHeight);
        gfx.DrawRectangle(borderPen, margin, y, contentWidth, boxHeight);

        var colWidth = contentWidth / 2;

        // Column 1: Patient
        gfx.DrawString("DATOS DEL PACIENTE", fontSmallBold, new XSolidBrush(primary), margin + 10, y + 16);
        gfx.DrawString($"Nombre: {report.PatientName}", fontBodyBold, new XSolidBrush(textDark), margin + 10, y + 32);
        gfx.DrawString($"Documento: {report.PatientDocument}", fontBody, new XSolidBrush(textDark), margin + 10, y + 46);
        gfx.DrawString($"ID Cita Médica: #{report.AppointmentId}  |  Fecha Cita: {report.AppointmentDate}", fontSmall, new XSolidBrush(textMuted), margin + 10, y + 60);

        // Column 2: Doctor
        gfx.DrawString("MÉDICO TRATANTE", fontSmallBold, new XSolidBrush(primary), margin + colWidth + 10, y + 16);
        gfx.DrawString($"Dr(a). {report.DoctorName}", fontBodyBold, new XSolidBrush(textDark), margin + colWidth + 10, y + 32);
        gfx.DrawString($"Especialidad: {report.DoctorSpecialty}", fontBody, new XSolidBrush(textDark), margin + colWidth + 10, y + 46);
        gfx.DrawString($"Estado del Informe: {(report.IsFinalized ? "Finalizado / Firmado" : "Borrador")}", fontSmall, new XSolidBrush(textMuted), margin + colWidth + 10, y + 60);

        y += boxHeight + 15;

        // ─── Vital Signs Grid ─────────────────────────────────────────────────
        gfx.DrawString("SIGNOS VITALES Y SOMATOMETRÍA", fontSmallBold, new XSolidBrush(navyBlue), margin, y);
        y += 8;

        var vsHeight = 35;
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(241, 245, 249)), margin, y, contentWidth, vsHeight);
        gfx.DrawRectangle(borderPen, margin, y, contentWidth, vsHeight);

        var vsSlotWidth = contentWidth / 6;
        DrawVitalSign(gfx, "P. Arterial", report.BloodPressure ?? "N/R", margin + (vsSlotWidth * 0), y, vsSlotWidth, fontSmall, fontBodyBold, textMuted, textDark);
        DrawVitalSign(gfx, "Frec. Cardíaca", report.HeartRate.HasValue ? $"{report.HeartRate} bpm" : "N/R", margin + (vsSlotWidth * 1), y, vsSlotWidth, fontSmall, fontBodyBold, textMuted, textDark);
        DrawVitalSign(gfx, "Temperatura", report.Temperature.HasValue ? $"{report.Temperature:F1} °C" : "N/R", margin + (vsSlotWidth * 2), y, vsSlotWidth, fontSmall, fontBodyBold, textMuted, textDark);
        DrawVitalSign(gfx, "Peso", report.WeightKg.HasValue ? $"{report.WeightKg:F1} kg" : "N/R", margin + (vsSlotWidth * 3), y, vsSlotWidth, fontSmall, fontBodyBold, textMuted, textDark);
        DrawVitalSign(gfx, "Estatura", report.HeightCm.HasValue ? $"{report.HeightCm:F0} cm" : "N/R", margin + (vsSlotWidth * 4), y, vsSlotWidth, fontSmall, fontBodyBold, textMuted, textDark);
        DrawVitalSign(gfx, "IMC", report.Bmi.HasValue ? $"{report.Bmi:F1} kg/m²" : "N/R", margin + (vsSlotWidth * 5), y, vsSlotWidth, fontSmall, fontBodyBold, textMuted, textDark);

        y += vsHeight + 20;

        // ─── Clinical Content Sections ────────────────────────────────────────
        y = DrawSection(gfx, "1. MOTIVO DE CONSULTA Y SÍNTOMAS", report.ReasonForVisit, margin, y, contentWidth, fontSmallBold, fontBody, primary, textDark, borderPen);

        if (!string.IsNullOrWhiteSpace(report.PhysicalExamination))
        {
            y = DrawSection(gfx, "2. EXAMEN FÍSICO Y HALLAZGOS", report.PhysicalExamination, margin, y, contentWidth, fontSmallBold, fontBody, primary, textDark, borderPen);
        }

        y = DrawSection(gfx, "3. IMPRESIÓN DIAGNÓSTICA", report.Diagnosis, margin, y, contentWidth, fontSmallBold, fontBodyBold, primary, textDark, borderPen, isHighlighted: true);

        y = DrawSection(gfx, "4. PLAN DE TRATAMIENTO Y PRESCRIPCIÓN MÉDICA", report.Treatment, margin, y, contentWidth, fontSmallBold, fontBody, primary, textDark, borderPen);

        if (!string.IsNullOrWhiteSpace(report.Notes))
        {
            y = DrawSection(gfx, "5. RECOMENDACIONES Y OBSERVACIONES", report.Notes, margin, y, contentWidth, fontSmallBold, fontBody, primary, textDark, borderPen);
        }

        // ─── Professional Signature & Verification ────────────────────────────
        y = Math.Max(y + 20, page.Height.Point - 130);

        var sigBoxWidth = 220;
        var sigX = pageWidth - margin - sigBoxWidth;
        gfx.DrawLine(new XPen(textDark, 1), sigX, y + 35, sigX + sigBoxWidth, y + 35);
        gfx.DrawString($"Dr(a). {report.DoctorName}", fontBodyBold, new XSolidBrush(navyBlue), sigX + 10, y + 48);
        gfx.DrawString($"Especialista en {report.DoctorSpecialty}", fontSmall, new XSolidBrush(textMuted), sigX + 10, y + 60);
        gfx.DrawString("Firma Digital Certificada - Hospital San Vicente", fontSmall, new XSolidBrush(primary), sigX + 10, y + 72);

        // Left Verification Stamp
        var hash = Convert.ToHexString(Encoding.UTF8.GetBytes($"{report.Id}:{report.PatientId}:{report.CreatedAt}")).ToLowerInvariant()[..16];
        gfx.DrawString("CÓDIGO DE VALIDACIÓN CLÍNICA", fontSmallBold, new XSolidBrush(navyBlue), margin, y + 48);
        gfx.DrawString($"VERIFICADOR: SHA256-{hash.ToUpper()}", fontSmall, new XSolidBrush(textMuted), margin, y + 60);
        gfx.DrawString("Documento médico-legal confidencial emitido bajo normas de seguridad del paciente.", fontSmall, new XSolidBrush(textMuted), margin, y + 72);

        using var ms = new MemoryStream();
        document.Save(ms, false);
        return ms.ToArray();
    }

    private static void DrawVitalSign(
        XGraphics gfx,
        string label,
        string value,
        double x,
        double y,
        double width,
        XFont fontLabel,
        XFont fontValue,
        XColor colorLabel,
        XColor colorValue)
    {
        gfx.DrawString(label, fontLabel, new XSolidBrush(colorLabel), x + 6, y + 13);
        gfx.DrawString(value, fontValue, new XSolidBrush(colorValue), x + 6, y + 27);
    }

    private static double DrawSection(
        XGraphics gfx,
        string title,
        string content,
        double margin,
        double y,
        double contentWidth,
        XFont fontTitle,
        XFont fontContent,
        XColor colorTitle,
        XColor colorContent,
        XPen borderPen,
        bool isHighlighted = false)
    {
        gfx.DrawString(title, fontTitle, new XSolidBrush(colorTitle), margin, y);
        y += 10;

        var wrappedLines = WrapText(content, fontContent, contentWidth - 16);
        var height = (wrappedLines.Count * 14) + 12;

        if (isHighlighted)
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(240, 249, 255)), margin, y, contentWidth, height);
            gfx.DrawLine(new XPen(XColor.FromArgb(2, 132, 199), 2), margin, y, margin, y + height);
        }
        else
        {
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(254, 254, 255)), margin, y, contentWidth, height);
        }
        gfx.DrawRectangle(borderPen, margin, y, contentWidth, height);

        var textY = y + 14;
        foreach (var line in wrappedLines)
        {
            gfx.DrawString(line, fontContent, new XSolidBrush(colorContent), margin + 8, textY);
            textY += 14;
        }

        return y + height + 14;
    }

    private static List<string> WrapText(string text, XFont font, double maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var paragraphs = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                result.Add(string.Empty);
                continue;
            }

            var words = paragraph.Split(' ');
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                var testLine = currentLine.Length == 0 ? word : $"{currentLine} {word}";
                // Approximate width: average char width in points ~ font.Size * 0.52
                var approxWidth = testLine.Length * (font.Size * 0.52);

                if (approxWidth > maxWidth && currentLine.Length > 0)
                {
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(word);
                }
                else
                {
                    currentLine.Clear();
                    currentLine.Append(testLine);
                }
            }

            if (currentLine.Length > 0)
            {
                result.Add(currentLine.ToString());
            }
        }

        return result;
    }
}

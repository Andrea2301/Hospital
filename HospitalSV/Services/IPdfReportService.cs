using HospitalSV.DTOs;

namespace HospitalSV.Services;

public interface IPdfReportService
{
    byte[] GenerateMedicalReportPdf(MedicalReportResponseDto report);
}

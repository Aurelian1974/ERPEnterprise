CREATE OR ALTER PROCEDURE core.usp_EmailMessage_Get
    @Id int
AS
BEGIN
    -- Mesajul de trimis si atasamentele lui (Worker-ul, 02.01, 4.7).
    SET NOCOUNT ON;

    SELECT m.Id, m.CompanyId, m.ToAddress, m.CcAddress, m.Subject, m.Body, m.IsHtml, m.Status FROM core.EmailMessage AS m WHERE m.Id = @Id;
    SELECT a.AttachmentId FROM core.EmailMessageAttachment AS a WHERE a.EmailMessageId = @Id ORDER BY a.AttachmentId;
END;

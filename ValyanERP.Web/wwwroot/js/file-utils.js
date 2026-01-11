// File download utilities for export functionality

/**
 * Downloads a file from base64 content
 * @param {string} fileName - The name of the file to download
 * @param {string} base64Content - The base64 encoded content
 * @param {string} mimeType - The MIME type of the file
 */
window.downloadFile = function (fileName, base64Content, mimeType) {
    const byteCharacters = atob(base64Content);
    const byteNumbers = new Array(byteCharacters.length);
    
    for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
    }
    
    const byteArray = new Uint8Array(byteNumbers);
    const blob = new Blob([byteArray], { type: mimeType });
    
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(link.href);
};

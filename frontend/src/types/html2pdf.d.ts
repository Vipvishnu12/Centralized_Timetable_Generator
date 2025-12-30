declare module 'html2pdf.js' {
  type Html2PdfOptions = {
    margin?: number;
    filename?: string;
    image?: { type: string; quality: number };
    html2canvas?: { scale: number };
    jsPDF?: { unit: string; format: string | string[]; orientation: string };
  };

  interface Html2PdfInstance {
    from: (element: HTMLElement | string) => Html2PdfInstance;
    set: (options: Html2PdfOptions) => Html2PdfInstance;
    save: (filename?: string) => Promise<void>;
    outputPdf?: () => Promise<Blob>;
    [key: string]: unknown;
  }

  const html2pdf: () => Html2PdfInstance;
  export default html2pdf;
}

import React, { useRef, useState } from 'react';
import { QRCodeSVG } from 'qrcode.react';
import styles from "./QRCodeGenerator.module.css";

interface QRCodeGeneratorProps {
    data: string;
    size?: number;
    bgColor?: string;
    fgColor?: string;
    level?: 'L' | 'M' | 'Q' | 'H';
    includeMargin?: boolean;
    imageSettings?: {
        src: string;
        height: number;
        width: number;
        excavate: boolean;
    };
    className?: string;
}

const QRCodeGenerator: React.FC<QRCodeGeneratorProps> = ({
                                                             data,
                                                             size = 200,
                                                             bgColor = '#FFFFFF',
                                                             fgColor = '#000000',
                                                             level = 'M',
                                                             includeMargin = false,
                                                             imageSettings,
                                                             className = ''
                                                         }) => {
    const qrRef = useRef<SVGSVGElement>(null);
    const [isDownloading, setIsDownloading] = useState(false);

    const downloadQRCode = async () => {
        if (!qrRef.current || !data || isDownloading) return;

        setIsDownloading(true);

        try {
            const svg = qrRef.current;
            const svgData = new XMLSerializer().serializeToString(svg);
            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d');

            // Set canvas dimensions
            canvas.width = size;
            canvas.height = size;

            // Create and load image
            const img = new Image();
            const svgBlob = new Blob([svgData], { type: 'image/svg+xml;charset=utf-8' });
            const url = URL.createObjectURL(svgBlob);

            await new Promise((resolve, reject) => {
                img.onload = resolve;
                img.onerror = reject;
                img.src = url;
            });

            if (ctx) {
                // Draw image on canvas
                ctx.drawImage(img, 0, 0, size, size);

                // Create download link
                const png = canvas.toDataURL('image/png');
                const downloadLink = document.createElement('a');
                downloadLink.download = 'qrcode.png';
                downloadLink.href = png;
                document.body.appendChild(downloadLink);
                downloadLink.click();
                document.body.removeChild(downloadLink);
            }

            // Cleanup
            URL.revokeObjectURL(url);
        } catch (error) {
            console.error('Error downloading QR code:', error);
        } finally {
            setIsDownloading(false);
        }
    };

    if (!data) {
        return (
            <div className={`${styles.container} ${styles.error} ${className}`}>
                <p>No data provided for QR code generation</p>
            </div>
        );
    }

    return (
        <div className={`${styles.container} ${className}`}>
            <QRCodeSVG
                ref={qrRef}
                value={data}
                size={size}
                bgColor={bgColor}
                fgColor={fgColor}
                level={level}
                imageSettings={imageSettings}
                title="QR Code"
            />

            {data && (
                <button
                    className={styles.downloadButton}
                    onClick={downloadQRCode}
                    disabled={isDownloading}
                >
                    {isDownloading ? 'Downloading...' : 'Download QR Code'}
                </button>
            )}
        </div>
    );
};

export default QRCodeGenerator;
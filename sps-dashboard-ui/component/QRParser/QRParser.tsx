import React, { useState, useRef } from 'react';
import styles from './QRParser.module.css';
import ActionButton from '../common/ActionButton/ActionButton';
import jsQR from 'jsqr';
import AlertModal from "../common/AlertModal/AlertModal";

type QRType = 'merchant' | 'personal' | null;

interface QRParserProps {
    title: string;
    subtitle: string;
    onParse: (qrCode: string) => Promise<unknown>;
    qrType: 'merchant' | 'personal';
}

interface QRValidationResult {
    isValid: boolean;
    type: QRType;
}

export const QRParser: React.FC<QRParserProps> = ({
                                                      title,
                                                      subtitle,
                                                      onParse,
                                                      qrType
                                                  }) => {
    const [qrCode, setQrCode] = useState<string>('');
    const [parseResult, setParseResult] = useState<string | object | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string>('');
    const fileInputRef = useRef<HTMLInputElement>(null);
    const [qrImage, setQrImage] = useState<string | null>(null);

    const scanQRCodeFromImage = async (file: File): Promise<string> => {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = async (event) => {
                try {
                    const img = new Image();
                    img.onload = () => {
                        const canvas = document.createElement('canvas');
                        canvas.width = img.width;
                        canvas.height = img.height;
                        const ctx = canvas.getContext('2d');

                        if (!ctx) {
                            reject(new Error('Could not get canvas context'));
                            return;
                        }

                        ctx.drawImage(img, 0, 0);
                        const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
                        const code = jsQR(imageData.data, imageData.width, imageData.height);

                        if (code) {
                            resolve(code.data);
                        } else {
                            reject(new Error('No QR code found in image'));
                        }
                    };
                    img.onerror = () => reject(new Error('Failed to load image'));
                    img.src = event.target?.result as string;
                } catch (err) {
                    reject(err);
                }
            };
            reader.onerror = () => reject(new Error('Failed to read file'));
            reader.readAsDataURL(file);
        });
    };

    const validateQRCode = (qr: string): QRValidationResult => {
        const trimmed = qr.trim();

        if (trimmed.startsWith('000201') && trimmed.length > 30) {
            return { isValid: true, type: 'merchant' };
        }

        if (trimmed.startsWith('000202') && trimmed.length > 30) {
            return { isValid: true, type: 'personal' };
        }

        return { isValid: false, type: null };
    };

    const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        try {
            const qrData = await scanQRCodeFromImage(file);
            const validation = validateQRCode(qrData);

            if (!validation.isValid) {
                throw new Error('Invalid QR code format');
            }

            if (validation.type !== qrType) {
                console.log('Invalid QR Code format ' + validation.type);
                throw new Error(`Please upload a ${qrType} QR code`);
            }

            setQrCode(qrData);
            setError('');

            // Show preview of the uploaded image
            const reader = new FileReader();
            reader.onload = (event) => {
                setQrImage(event.target?.result as string);
            };
            reader.readAsDataURL(file);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to read QR code');
            setQrImage(null);
            setQrCode('');
            if (fileInputRef.current) fileInputRef.current.value = '';
        }
    };

    const handleParse = async () => {
        if (!qrCode.trim()) {
            setError('Please enter a QR code or upload an image');
            return;
        }

        const validation = validateQRCode(qrCode);
        if (!validation.isValid) {
            setError('Invalid QR code format');
            return;
        }

        if (validation.type !== qrType) {
            setError(`Please provide a ${qrType} QR code`);
            return;
        }

        setIsLoading(true);
        setError('');
        setParseResult(null);

        try {
            const result = await onParse(qrCode);
            // @ts-ignore
            setParseResult(result);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to parse QR code');
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className={styles.container}>
            <h2 className={styles.title}>{title}</h2>
            <p className={styles.subtitle}>{subtitle}</p>

            <div className={styles.inputSection}>
                <div className={styles.inputGroup}>
                    <label className={styles.label}>QR Code Data</label>
                    <textarea
                        className={styles.textarea}
                        value={qrCode}
                        onChange={(e) => {
                            setQrCode(e.target.value);
                            setError('');
                        }}
                        placeholder="Paste QR code data here or upload an image"
                        rows={4}
                    />
                </div>

                <div className={styles.uploadSection}>
                    <input
                        type="file"
                        ref={fileInputRef}
                        onChange={handleFileUpload}
                        accept="image/*"
                        className={styles.fileInput}
                    />
                    <ActionButton
                        onClick={() => fileInputRef.current?.click()}
                        className={styles.uploadButton}
                    >
                        Upload QR Image
                    </ActionButton>
                    {qrImage && (
                        <div className={styles.qrPreview}>
                            {/* eslint-disable-next-line @next/next/no-img-element */}
                            <img src={qrImage} alt="Uploaded QR" className={styles.qrImage} />
                            <button
                                className={styles.removeButton}
                                onClick={() => {
                                    setQrImage(null);
                                    setQrCode('');
                                    if (fileInputRef.current) fileInputRef.current.value = '';
                                }}
                            >
                                Remove
                            </button>
                        </div>
                    )}
                </div>
            </div>

            <ActionButton
                onClick={handleParse}
                disabled={isLoading}
                className={styles.parseButton}
            >
                {isLoading ? 'Parsing...' : 'Parse QR Code'}
            </ActionButton>

            {parseResult && (
                <div className={styles.resultSection}>
                    <h3 className={styles.resultTitle}>Parsed QR Data</h3>
                    <div className={styles.resultData}>
                        <pre>{JSON.stringify(parseResult, null, 2)}</pre>
                    </div>
                </div>
            )}

            {error && (
                <AlertModal
                    title="Error"
                    message={error}
                    onConfirm={() => setError('')}
                    onClose={() => setError('')}
                    error
                />
            )}
        </div>
    );
};
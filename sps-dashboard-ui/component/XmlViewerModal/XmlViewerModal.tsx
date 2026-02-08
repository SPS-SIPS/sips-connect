import React from 'react';
import ActionButton from "../common/ActionButton/ActionButton";
import styles from './XmlViewerModal.module.css';
import {AiOutlineClose} from "react-icons/ai";

interface XmlViewerModalProps {
    content: string;
    title: string;
    onClose: () => void;
}

const XmlViewerModal: React.FC<XmlViewerModalProps> = ({content, title, onClose}) => {
    const handleCopy = () => {
        navigator.clipboard.writeText(content);
    };

    return (
        <div className={styles.modalOverlay}>
            <div className={styles.modalContent}>
                <div className={styles.modalHeader}>
                    <h4>{title}</h4>
                    <div className={styles.modalActions}>
                        <ActionButton
                            onClick={handleCopy}
                            className={styles.copyButton}
                        >
                            Copy XML
                        </ActionButton>
                        <button
                            onClick={onClose}
                            className={styles.closeButton}
                        >
                            <AiOutlineClose size={24} />
                        </button>
                    </div>
                </div>
                <pre className={styles.xmlPreview}>
        {content}
        </pre>
            </div>
        </div>
    );
};

export default XmlViewerModal;
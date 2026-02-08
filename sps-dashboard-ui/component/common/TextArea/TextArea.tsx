import React from 'react';
import styles from "./TextArea.module.css";

interface TextAreaProps {
    label: string;
    value: string;
    onChange: (e: React.ChangeEvent<HTMLTextAreaElement>) => void;
    required?: boolean;
    placeholder?: string;
    maxLength?: number;
    errorMessage?: string;
    name?: string;
}

const TextArea: React.FC<TextAreaProps> = ({
                                               label,
                                               value,
                                               onChange,
                                               required = false,
                                               placeholder,
                                               maxLength,
                                               errorMessage,
                                               name
                                           }) => {
    return (
        <div className={styles.textAreaContainer}>
            <label className={styles.label}>
                {label}
                {required && <span className={styles.requiredAsterisk}>*</span>}
            </label>
            <textarea
                className={styles.textAreaField}
                value={value}
                onChange={onChange}
                placeholder={placeholder}
                required={required}
                maxLength={maxLength}
                name={name}
            />
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default TextArea;

import React from 'react';
import styles from "./Input.module.css"

interface InputProps {
    label: string;
    value: string;
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
    type: 'text' | 'password' | 'email' | 'number' | 'tel' | 'url' | 'date' | 'datetime-local' | string;
    required?: boolean;
    autoComplete?: string;
    placeholder?: string;
    name?: string;
    errorMessage?: string;
    maxLength?: number;
    disabled?: boolean;
    min?: number;
}

const Input: React.FC<InputProps> = ({
                                         label,
                                         value,
                                         onChange,
                                         type,
                                         required = false,
                                         autoComplete = 'off',
                                         placeholder,
                                         name,
                                         errorMessage,
                                         maxLength,
                                         disabled = false,
                                         min,
                                     }) => {
    return (
        <div className={styles.inputContainer}>
            {label &&
                <label className={styles.label}>
                    {label}
                    {required && <span className={styles.requiredAsterisk}>*</span>}
                </label>
            }
            <div className={styles.inputWrapper}>
                <input
                    className={styles.inputField}
                    type={type}
                    name={name}
                    value={value}
                    onChange={onChange}
                    placeholder={placeholder}
                    required={required}
                    autoComplete={autoComplete}
                    maxLength={maxLength}
                    disabled={disabled}
                    min={type === 'number' ? min : undefined}
                />
            </div>
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default Input;

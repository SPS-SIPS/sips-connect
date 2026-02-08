import React from 'react';
import styles from "./RadioInput.module.css";

interface RadioInputProps {
    label: string;
    options: { value: string; label: string }[];
    selectedValue: string;
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
    required?: boolean;
    errorMessage?: string;
    name?: string;
}

const RadioInput: React.FC<RadioInputProps> = ({
                                                   label,
                                                   options,
                                                   selectedValue,
                                                   onChange,
                                                   required = false,
                                                   errorMessage,
                                                   name
                                               }) => {
    return (
        <div className={styles.radioContainer}>
            <label className={styles.label}>
                {label}
                {required && <span className={styles.requiredAsterisk}>*</span>}
            </label>
            <div className={styles.radioGroup}>
                {options.map(option => (
                    <label key={option.value} className={styles.radioOption}>
                        <input
                            type="radio"
                            value={option.value}
                            checked={selectedValue === option.value}
                            onChange={onChange}
                            required={required}
                            name={name}
                            className={styles.radioInput}
                        />
                        {option.label}
                    </label>
                ))}
            </div>
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default RadioInput;

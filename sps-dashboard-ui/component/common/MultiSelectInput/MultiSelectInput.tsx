import React from 'react';
import styles from "./MultiSelectInput.module.css";

interface MultiSelectInputProps {
    label: string;
    options: { value: string; label: string }[];
    selectedValues: string[];
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
    required?: boolean;
    errorMessage?: string;
    name?: string;
}

const MultiSelectInput: React.FC<MultiSelectInputProps> = ({
                                                               label,
                                                               options,
                                                               selectedValues,
                                                               onChange,
                                                               required = false,
                                                               errorMessage,
                                                               name
                                                           }) => {
    return (
        <div className={styles.checkboxContainer}>
            <label className={styles.label}>
                {label}
                {required && <span className={styles.requiredAsterisk}>*</span>}
            </label>
            <div className={styles.checkboxGroup}>
                {options.map(option => (
                    <label key={option.value} className={styles.checkboxOption}>
                        <input
                            type="checkbox"
                            value={option.value}
                            checked={selectedValues.includes(option.value)}
                            onChange={onChange}
                            required={required}
                            name={name}
                            className={styles.checkboxInput}
                        />
                        {option.label}
                    </label>
                ))}
            </div>
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default MultiSelectInput;

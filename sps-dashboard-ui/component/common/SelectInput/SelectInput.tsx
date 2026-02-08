import React from 'react';
import styles from "./SelectInput.module.css";

interface SelectInputProps {
    label: string;
    value: string;
    onChange: (e: React.ChangeEvent<HTMLSelectElement>) => void;
    options: { value: string; label: string }[];
    required?: boolean;
    errorMessage?: string;
    name?: string;
    disabled?: boolean;
    placeholder?: string;
}

const SelectInput: React.FC<SelectInputProps> = ({
                                                     label,
                                                     value,
                                                     onChange,
                                                     options,
                                                     required = false,
                                                     errorMessage,
                                                     name,
                                                     disabled = false,
                                                     placeholder = "-- Select an option --"
                                                 }) => {
    return (
        <div className={styles.inputContainer}>
            <label className={styles.label}>
                {label}
                {required && <span className={styles.requiredAsterisk}>*</span>}
            </label>
            <div className={styles.selectWrapper}>
                <select
                    className={styles.inputField}
                    value={value}
                    onChange={onChange}
                    required={required}
                    name={name}
                    disabled={disabled}
                >
                    <option value="" disabled>
                        {placeholder}
                    </option>
                    {options.map(option => (
                        <option key={option.value} value={option.value}>
                            {option.label}
                        </option>
                    ))}
                </select>
            </div>
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default SelectInput;

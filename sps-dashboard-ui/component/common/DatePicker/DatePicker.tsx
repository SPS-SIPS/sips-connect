import React, {useRef} from 'react';
import styles from './DatePicker.module.css';

interface DatePickerProps {
    label: string;
    value: string | null;
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
    required?: boolean;
    autoComplete?: string;
    placeholder?: string;
    name?: string;
    errorMessage?: string;
    min?: string | null;
    max?: string | null;
    disabled?: boolean;
}

const DatePicker: React.FC<DatePickerProps> = ({
                                                   label,
                                                   value,
                                                   onChange,
                                                   required = false,
                                                   autoComplete,
                                                   placeholder,
                                                   name,
                                                   errorMessage,
                                                   min,
                                                   max,
                                                   disabled,
                                               }) => {
    const inputRef = useRef<HTMLInputElement>(null);

    const handleWrapperClick = () => {
        if (inputRef.current) {
            inputRef.current.showPicker();
        }
    };

    return (
        <div className={styles.inputContainer}>
            <label className={styles.label}>
                {label}
                {required && <span className={styles.requiredAsterisk}>*</span>}
            </label>
            <div
                className={styles.inputWrapper}
                onClick={handleWrapperClick}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => e.key === 'Enter' && handleWrapperClick()}
            >
                <input
                    className={styles.inputField}
                    type="date"
                    ref={inputRef}
                    name={name}
                    value={value ?? ''}
                    onChange={onChange}
                    placeholder={placeholder}
                    required={required}
                    autoComplete={autoComplete}
                    max={max ?? ''}
                    min={min ?? ''}
                    disabled={disabled}
                />
            </div>
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default DatePicker;
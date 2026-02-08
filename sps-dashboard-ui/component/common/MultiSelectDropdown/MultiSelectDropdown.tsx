import React from 'react';
import Select from 'react-select';
import styles from './MultiSelectDropdown.module.css';

export interface OptionType {
    value: string;
    label: string;
}

interface MultiSelectDropdownProps {
    label: string;
    value: OptionType[] | null;
    onChange: (selected: OptionType[] | null) => void;
    options: OptionType[];
    required?: boolean;
    errorMessage?: string;
    name?: string;
    isDisabled?: boolean;
    placeholder?: string;
}

const MultiSelectDropdown: React.FC<MultiSelectDropdownProps> = ({
                                                                     label,
                                                                     value,
                                                                     onChange,
                                                                     options,
                                                                     required = false,
                                                                     errorMessage,
                                                                     name,
                                                                     isDisabled = false,
                                                                     placeholder = '-- Select an option --',
                                                                 }) => {
    return (
        <div className={styles.inputContainer}>
            <label className={styles.label}>
                {label}
                {required && <span className={styles.requiredAsterisk}>*</span>}
            </label>
            <div className={styles.selectWrapper}>
                <Select
                    isMulti
                    classNamePrefix="custom-select"
                    value={value}
                    onChange={(selected) => onChange(selected as OptionType[])}
                    options={options}
                    isDisabled={isDisabled}
                    name={name}
                    placeholder={placeholder}
                    isClearable
                    styles={{
                        control: (base, state) => ({
                            ...base,
                            width: '100%',
                            backgroundColor: 'var(--color-text-light)',
                            borderColor: state.isFocused
                                ? 'var(--color-border-focus)'
                                : 'var(--color-border)',
                            boxShadow: state.isFocused ? 'var(--shadow-focus)' : 'none',
                            '&:hover': {
                                borderColor: 'var(--color-primary-hover)',
                            },
                            padding: 'var(--spacing-xxs)',
                            borderRadius: 'var(--radius-md)',
                            minHeight: '40px',
                            fontSize: 'var(--font-size-base)',
                        }),
                        option: (base, state) => ({
                            ...base,
                            backgroundColor: state.isSelected
                                ? 'var(--color-primary-light)'
                                : state.isFocused
                                    ? 'var(--color-background-accent)'
                                    : 'var(--color-white)',
                            color: 'var(--color-text-primary)',
                            cursor: 'pointer',
                            fontSize: 'var(--font-size-base)',
                            padding: 'var(--spacing-sm) var(--spacing-md)',
                        }),
                        multiValue: (base) => ({
                            ...base,
                            backgroundColor: 'var(--color-primary-light)',
                            borderRadius: 'var(--radius-sm)',
                        }),
                        multiValueLabel: (base) => ({
                            ...base,
                            color: 'var(--color-text-primary)',
                            fontWeight: '500',
                            padding: 'var(--spacing-xxs) var(--spacing-xs)',
                        }),
                        multiValueRemove: (base) => ({
                            ...base,
                            color: 'var(--color-primary-dark)',
                            ':hover': {
                                backgroundColor: 'var(--color-primary-hover)',
                                color: 'var(--color-white)',
                            },
                        }),
                        placeholder: (base) => ({
                            ...base,
                            color: 'var(--color-placeholder)',
                            fontSize: 'var(--font-size-base)',
                        }),
                        singleValue: (base) => ({
                            ...base,
                            color: 'var(--color-text-primary)',
                            fontSize: 'var(--font-size-base)',
                        }),
                        menu: (base) => ({
                            ...base,
                            minWidth: '100%',
                            zIndex: 'var(--z-index-dropdown)',
                            boxShadow: 'var(--shadow-md)',
                            borderRadius: 'var(--radius-md)',
                        }),
                        menuList: (base) => ({
                            ...base,
                            padding: 'var(--spacing-xs) 0',
                        }),
                        indicatorSeparator: (base) => ({
                            ...base,
                            backgroundColor: 'var(--color-border)',
                        }),
                        dropdownIndicator: (base) => ({
                            ...base,
                            color: 'var(--color-placeholder)',
                            ':hover': {
                                color: 'var(--color-text-secondary)',
                            },
                        }),
                    }}
                />
            </div>
            {errorMessage && <div className={styles.errorMessage}>{errorMessage}</div>}
        </div>
    );
};

export default MultiSelectDropdown;
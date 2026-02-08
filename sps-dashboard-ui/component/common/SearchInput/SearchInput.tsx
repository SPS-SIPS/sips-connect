import React from 'react';
import styles from './SearchInput.module.css';
import {FiSearch} from "react-icons/fi";

interface SearchInputProps {
    value: string;
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
    placeholder?: string;
    required?: boolean;
}

const SearchInput: React.FC<SearchInputProps> = ({
                                                     value,
                                                     onChange,
                                                     placeholder = "Search...",
                                                     required = false,
                                                 }) => {
    return (
        <div className={styles.inputContainer}>
            <div className={styles.inputWrapper}>
                <input
                    className={styles.inputField}
                    type="text"
                    value={value}
                    onChange={onChange}
                    placeholder={placeholder}
                    required={required}
                />
                <div className={styles.searchIcon}>
                    <div className={styles.searchIcon}>
                        <FiSearch size={16} color="gray" />
                    </div>
                </div>
            </div>
        </div>
    );
};

export default SearchInput;

import React from 'react';
import styles from './ActionButton.module.css';

interface ActionButtonProps {
    type?: "button" | "submit" | "reset";
    onClick?: () => void;
    children: React.ReactNode;
    className?: string;
    disabled?: boolean;
}

const ActionButton: React.FC<ActionButtonProps> = ({
                                                       type = "button",
                                                       onClick,
                                                       children,
                                                       className,
                                                       disabled = false
                                                   }) => {
    return (
        <button
            type={type}
            onClick={onClick}
            className={`${styles.actionButton} ${className}`}
            disabled={disabled}>
            {children}
        </button>
    );
};

export default ActionButton;

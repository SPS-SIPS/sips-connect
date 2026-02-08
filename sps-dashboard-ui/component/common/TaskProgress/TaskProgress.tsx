import React from 'react';
import styles from './TaskProgress.module.css';

interface ProgressBarProps {
    percentage: number;
    color?: string;
    height?: number;
    showStatus?: boolean;
    status?: 'success' | 'error' | 'warning';
    label?: string;
    labelPosition?: 'inside' | 'outside';
    rounded?: boolean;
    borderRadius?: number;
}

const TaskProgress: React.FC<ProgressBarProps> = ({
                                                     percentage,
                                                     color = 'var(--color-primary)',
                                                     height = 8,
                                                     showStatus = false,
                                                     status,
                                                     label,
                                                     labelPosition = 'outside',
                                                     rounded = true,
                                                     borderRadius = 8,
                                                 }) => {
    const progressColor = status ?
        `var(--color-${status})` :
        color;

    const clampedPercentage = Math.min(Math.max(percentage, 0), 100);

    const getStatusIcon = () => {
        if (!status) return null;

        const iconColor = `var(--color-${status})`;

        switch (status) {
            case 'success':
                return (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
                        <path d="M5 12l5 5L20 7" stroke={iconColor} strokeWidth="2" strokeLinecap="round"/>
                    </svg>
                );
            case 'error':
                return (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
                        <path d="M6 18L18 6M6 6l12 12" stroke={iconColor} strokeWidth="2" strokeLinecap="round"/>
                    </svg>
                );
            case 'warning':
                return (
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
                        <path d="M12 9v4m0 4h.01" stroke={iconColor} strokeWidth="2" strokeLinecap="round"/>
                        <circle cx="12" cy="12" r="9" stroke={iconColor} strokeWidth="2"/>
                    </svg>
                );
            default:
                return null;
        }
    };

    return (
        <div className={styles.container}>
            {label && labelPosition === 'outside' && (
                <div className={styles.label}>{label}</div>
            )}

            <div
                className={styles.progressTrack}
                style={{
                    height: `${height}px`,
                    borderRadius: rounded ?
                        `${borderRadius}px` :
                        'var(--radius-sm)',
                    backgroundColor: 'var(--color-background-secondary)',
                }}
                role="progressbar"
                aria-valuenow={clampedPercentage}
                aria-valuemin={0}
                aria-valuemax={100}
            >
                <div
                    className={styles.progressFill}
                    style={{
                        width: `${clampedPercentage}%`,
                        backgroundColor: progressColor,
                        borderRadius: rounded ?
                            `${borderRadius}px` :
                            'var(--radius-sm)',
                        transition: 'width var(--transition-normal)',
                    }}
                >
                    {label && labelPosition === 'inside' && (
                        <span
                            className={styles.labelInside}
                            style={{ color: 'var(--color-text-light)' }}
                        >
                            {label}
                        </span>
                    )}
                </div>

                {showStatus && status && (
                    <div className={styles.statusIcon}>
                        {getStatusIcon()}
                    </div>
                )}
            </div>
        </div>
    );
};

export default TaskProgress;
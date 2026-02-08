import React from 'react';
import styles from './SpinLoading.module.css';

interface SpinLoadingProps {
    size?: number;
    color?: string;
    speed?: number;
    thickness?: number;
}

const SpinLoading: React.FC<SpinLoadingProps> = ({
                                                     size = 40,
                                                     color = '#568e43',
                                                     speed = 1,
                                                     thickness = 4,
                                                 }) => {
    return (
        <div
            className={styles.spinner}
            style={{
                width: `${size}px`,
                height: `${size}px`,
                border: `${thickness}px solid ${color}`,
                borderTop: `${thickness}px solid transparent`,
                animationDuration: `${speed}s`,
            }}
        ></div>
    );
};

export default SpinLoading;
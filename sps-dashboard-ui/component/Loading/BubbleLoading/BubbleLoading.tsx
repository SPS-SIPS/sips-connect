import React from 'react';
import styles from './BubbleLoading.module.css';

interface SpinBubbleLoadingProps {
    size?: number;
    color?: string;
    speed?: number;
    gap?: number;
}

const BubbleLoading: React.FC<SpinBubbleLoadingProps> = ({
                                                                 size = 16,
                                                                 color = '#568e43',
                                                                 speed = 0.6,
                                                                 gap = 8,
                                                             }) => {
    return (
        <div className={styles.overlay}>
            <div className={styles.loaderContainer} style={{ gap: `${gap}px` }}>
                {[0, 1, 2].map((index) => (
                    <div
                        key={index}
                        className={styles.bubble}
                        style={{
                            width: `${size}px`,
                            height: `${size}px`,
                            backgroundColor: color,
                            animationDuration: `${speed}s`,
                            animationDelay: `${index * (speed / 3)}s`,
                        }}
                    ></div>
                ))}
            </div>
        </div>
    );
};

export default BubbleLoading;

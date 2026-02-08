import {useState, useEffect, ReactNode} from 'react';
import styles from "./Layout.module.css";
import {Navbar} from "./Navbar/Navbar";
import Sidebar from "./Sidebar/Sidebar";
import Footer from "./Footer/Footer";

interface LayoutProps {
    children: ReactNode;
}

export const Layout = ({children}: LayoutProps) => {
    const [isSidebarOpen, setIsSidebarOpen] = useState(true);
    const [isMobile, setIsMobile] = useState(false);

    // Check mobile state on resize
    useEffect(() => {
        const handleResize = () => {
            const mobile = window.innerWidth < 768;
            setIsMobile(mobile);
            if (mobile && isSidebarOpen) {
                setIsSidebarOpen(false);
            }
        };

        handleResize(); // Initial check
        window.addEventListener('resize', handleResize);
        return () => window.removeEventListener('resize', handleResize);
    }, []);
    const handleSidebarToggle = () => {
        setIsSidebarOpen(!isSidebarOpen);
    };
    return (
        <div className={styles.layout}>
            <div className={styles.navbarContainer}>
                <Navbar
                    onMenuToggle={() => setIsSidebarOpen(!isSidebarOpen)}
                    isMobile={isMobile}
                />
            </div>

            <div className={styles.contentContainer}>

                <div className={styles.sidebarContainer}>
                    <Sidebar
                        isOpen={isSidebarOpen}
                        onClose={handleSidebarToggle}
                        isMobile={isMobile}
                    />
                </div>

                <main className={styles.mainContent}>
                    {children}
                </main>
            </div>
            <div className={styles.footerContainer}>
                <Footer/>
            </div>
        </div>
    );
};
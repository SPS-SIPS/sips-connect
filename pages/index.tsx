import React from 'react';
import {FiChevronRight} from 'react-icons/fi';
import {FaFileAlt, FaPlug, FaCogs, FaRocket, FaPlusCircle, FaEye} from 'react-icons/fa';
import styles from '../styles/Home.module.css';
import Link from "next/link";

const DashboardHome = () => {
    return (
      <>
          <div className={styles.container}>
              {/* Header Section */}
              <header className={styles.header}>
                  <h1 className={styles.title}>SIPS Connect API Dashboard</h1>
                  <p className={styles.subtitle}>
                      Configure adapters, test endpoints, and manage API integrations
                  </p>
              </header>

              {/* Quick Access Grid */}
              <section className={styles.quickAccess}>
                  <div className={styles.card}>
                      <div className={styles.cardHeader}>
                          <FaCogs className={styles.cardIcon}/>
                          <div className={styles.cardTitleWrapper}>
                              <h2>Request Builder</h2>
                              <span className={styles.featureBadge}>Featured</span>
                          </div>
                      </div>

                      <div className={styles.builderDescription}>
                          <p>Create and customize API requests with our visual builder</p>
                          <ul className={styles.builderFeatures}>
                              <li><FiChevronRight/> Build payment requests</li>
                              <li><FiChevronRight/> Generate verification payloads</li>
                              <li><FiChevronRight/> Save request templates</li>
                          </ul>
                      </div>

                      <div className={styles.cardFooter}>
                          <Link href={"/request-builder"}
                                className={styles.primaryButton}
                          >
                              Start Building
                          </Link>
                      </div>
                  </div>

                  {/* API Testing Suite Card */}
                  <div className={styles.card}>
                      <div className={styles.cardHeader}>
                          <FaRocket className={styles.cardIcon}/>
                          <h2>API Testing Suite</h2>
                      </div>
                      <div className={styles.subSections}>
                          <div className={styles.subCard}>
                              <h3>Transaction APIs</h3>
                              <ul className={styles.featureList}>
                                  <li><FiChevronRight/> Verify Transactions</li>
                                  <li><FiChevronRight/> Process Payments</li>
                                  <li><FiChevronRight/> Check Status</li>
                              </ul>
                          </div>

                          <div className={styles.subCard}>
                              <h3>QR Code Services</h3>
                              <div className={styles.qrSubGrid}>
                                  <Link href="/api-tester/qr-codes/generate/merchant" className={styles.miniCard}>
                                      <FaPlusCircle/> Generate QR Codes
                                  </Link>
                                  <Link href={"/api-tester/qr-codes/parse/merchant"} className={styles.miniCard}>
                                      <FaEye/> Parse QR Codes
                                  </Link>
                              </div>
                          </div>
                      </div>
                  </div>
              </section>

              {/* Configuration Section */}
              <section className={styles.configSection}>
                  <div className={styles.card}>
                      <div className={styles.cardHeader}>
                          <FaPlug className={styles.cardIcon}/>
                          <h2>Endpoint Configurations</h2>
                      </div>
                      <Link href="/config/endpoint" style={{textDecoration: 'none', color: 'inherit'}}>
                          <ul className={styles.configList}>
                              <li>
                                  <FiChevronRight/>
                                  <span>API Adapter Mappings</span>
                              </li>
                          </ul>
                      </Link>
                  </div>

                  <div className={styles.card}>
                      <div className={styles.cardHeader}>
                          <FaFileAlt className={styles.cardIcon}/>
                          <h2>Documentation Hub</h2>
                      </div>
                      <div className={styles.docGrid}>
                          <div className={styles.docCard}>
                              <h3>Getting Started</h3>
                              <p>Integration guide and setup instructions</p>
                          </div>
                          <div className={styles.docCard}>
                              <h3>API Reference</h3>
                              <p>Detailed endpoint documentation</p>
                          </div>
                      </div>
                  </div>
              </section>

              {/* System Status */}
              <section className={styles.statusSection}>
                  <div className={styles.statusCard}>
                      <h2>System Health</h2>
                      <div className={styles.statusGrid}>
                          <div className={styles.statusItem}>
                              <div className={styles.statusIndicator} data-status="ok"></div>
                              <span>API Gateway</span>
                          </div>
                          <div className={styles.statusItem}>
                              <div className={styles.statusIndicator} data-status="ok"></div>
                              <span>Database</span>
                          </div>
                          <div className={styles.statusItem}>
                              <div className={styles.statusIndicator} data-status="ok"></div>
                              <span>Authentication</span>
                          </div>
                      </div>
                  </div>
              </section>
          </div>
      </>
    );
};

export default DashboardHome;
-- REVIEW AND APPLY MANUALLY AFTER BACKUP. Never run automatically on startup.
-- Transactional engines are required for one-time codes and atomic account creation.
ALTER TABLE classicrealmd.account ENGINE=InnoDB;
ALTER TABLE classicrealmd.realmcharacters ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS classicrealmd.frostbound_verification (
 id BINARY(32) PRIMARY KEY,
 username VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 email VARCHAR(254) NOT NULL,
 purpose VARCHAR(8) CHARACTER SET ascii NOT NULL,
 code_hash BINARY(32) NOT NULL,
 expires_at DATETIME NOT NULL,
 sent_at DATETIME NOT NULL,
 window_start DATETIME NOT NULL,
 sends INT UNSIGNED NOT NULL,
 attempts INT UNSIGNED NOT NULL
) ENGINE=InnoDB;
-- Least privilege for a separately created service identity (substitute user/host yourself):
-- GRANT SELECT, INSERT, UPDATE ON classicrealmd.account TO 'SERVICE_USER'@'SERVICE_HOST';
-- GRANT SELECT ON classicrealmd.realmlist TO 'SERVICE_USER'@'SERVICE_HOST';
-- GRANT INSERT ON classicrealmd.realmcharacters TO 'SERVICE_USER'@'SERVICE_HOST';
-- GRANT SELECT, INSERT, UPDATE ON classicrealmd.frostbound_verification TO 'SERVICE_USER'@'SERVICE_HOST';
-- GRANT SELECT ON classiccharacters.characters TO 'SERVICE_USER'@'SERVICE_HOST';
